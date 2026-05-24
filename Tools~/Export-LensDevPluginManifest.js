#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..");
const pluginRoot = path.join(repoRoot, ".agents", "plugins", "lens-dev-plugin");
const manifestPath = path.join(pluginRoot, "manifest.json");
const pluginJsonPath = path.join(pluginRoot, ".codex-plugin", "plugin.json");
const mcpJsonPath = path.join(pluginRoot, ".mcp.json");
const programPath = path.join(repoRoot, "UnityMcpLensApp~", "src", "UnityMcpLens", "Program.cs");
const toolsRoot = path.join(repoRoot, "Editor", "Lens", "Tools");
const toolPackCatalogPath = path.join(repoRoot, "Editor", "Lens", "Lens", "ToolPackCatalog.cs");

const requiredFacadeTools = [
  "Unity_Tools_List",
  "Unity_Tools_Invoke",
  "Unity_Tools_BatchInvoke",
  "Unity_Tools_Describe",
  "Unity_Tools_Menu",
];

const args = new Set(process.argv.slice(2));
const write = args.has("--write");
const check = args.has("--check");
if (write && check) {
  console.error("Use either --write or --check, not both.");
  process.exit(2);
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function normalizeToolName(name) {
  return String(name || "").trim().replace(/\./g, "_");
}

function compactText(value) {
  const compact = String(value || "")
    .replace(/[^\x09\x0a\x0d\x20-\x7e]/g, "")
    .replace(/\s+/g, " ")
    .trim();
  return compact.length > 420 ? `${compact.slice(0, 417).trimEnd()}...` : compact;
}

function listFiles(dir, suffix) {
  const results = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...listFiles(fullPath, suffix));
    } else if (entry.isFile() && entry.name.endsWith(suffix)) {
      results.push(fullPath);
    }
  }
  return results.sort((a, b) => a.localeCompare(b));
}

function decodeCSharpStringLiteral(literal) {
  const trimmed = literal.trim();
  if (trimmed.startsWith("@\"")) {
    return trimmed.slice(2, -1).replace(/""/g, "\"");
  }

  if (!trimmed.startsWith("\"")) return null;
  return trimmed
    .slice(1, -1)
    .replace(/\\n/g, "\n")
    .replace(/\\r/g, "\r")
    .replace(/\\t/g, "\t")
    .replace(/\\"/g, "\"")
    .replace(/\\\\/g, "\\");
}

function collectStringConstants(text, prefix = "") {
  const constants = new Map();
  const regex = /(?:public|private|internal|protected|static|readonly|\s)*const\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(@?"(?:[^"]|"")*"|"(?:\\.|[^"\\])*")\s*;/gs;
  for (const match of text.matchAll(regex)) {
    const value = decodeCSharpStringLiteral(match[2]);
    if (value == null) continue;
    constants.set(match[1], value);
    if (prefix) constants.set(`${prefix}.${match[1]}`, value);
  }
  return constants;
}

function mergeConstants(...maps) {
  const merged = new Map();
  for (const map of maps) {
    for (const [key, value] of map.entries()) {
      merged.set(key, value);
    }
  }
  return merged;
}

function splitTopLevel(value, delimiter) {
  const parts = [];
  let start = 0;
  let depth = 0;
  let inString = false;
  let verbatim = false;
  for (let index = 0; index < value.length; index++) {
    const char = value[index];
    const prev = index > 0 ? value[index - 1] : "";
    if (inString) {
      if (verbatim && char === "\"" && value[index + 1] === "\"") {
        index++;
        continue;
      }
      if (char === "\"" && (verbatim || prev !== "\\")) {
        inString = false;
        verbatim = false;
      }
      continue;
    }

    if (char === "@" && value[index + 1] === "\"") {
      inString = true;
      verbatim = true;
      index++;
      continue;
    }
    if (char === "\"") {
      inString = true;
      continue;
    }
    if (char === "(" || char === "[" || char === "{") depth++;
    if (char === ")" || char === "]" || char === "}") depth--;
    if (char === delimiter && depth === 0) {
      parts.push(value.slice(start, index).trim());
      start = index + 1;
    }
  }
  parts.push(value.slice(start).trim());
  return parts.filter(Boolean);
}

function evaluateStringExpression(expression, constants) {
  let expr = expression.trim();
  while (expr.startsWith("(") && expr.endsWith(")")) {
    expr = expr.slice(1, -1).trim();
  }

  const plusParts = splitTopLevel(expr, "+");
  if (plusParts.length > 1) {
    const values = plusParts.map((part) => evaluateStringExpression(part, constants));
    return values.every((value) => value != null) ? values.join("") : null;
  }

  if (expr.startsWith("\"") || expr.startsWith("@\"")) {
    return decodeCSharpStringLiteral(expr);
  }

  if (constants.has(expr)) {
    return constants.get(expr);
  }

  const localName = expr.split(".").pop();
  if (constants.has(localName)) {
    return constants.get(localName);
  }

  return null;
}

function findMcpToolArguments(text) {
  const results = [];
  const marker = "[McpTool(";
  let searchFrom = 0;
  while (true) {
    const markerIndex = text.indexOf(marker, searchFrom);
    if (markerIndex < 0) break;
    let index = markerIndex + marker.length;
    let depth = 1;
    let inString = false;
    let verbatim = false;
    for (; index < text.length; index++) {
      const char = text[index];
      const prev = index > 0 ? text[index - 1] : "";
      if (inString) {
        if (verbatim && char === "\"" && text[index + 1] === "\"") {
          index++;
          continue;
        }
        if (char === "\"" && (verbatim || prev !== "\\")) {
          inString = false;
          verbatim = false;
        }
        continue;
      }

      if (char === "@" && text[index + 1] === "\"") {
        inString = true;
        verbatim = true;
        index++;
        continue;
      }
      if (char === "\"") {
        inString = true;
        continue;
      }
      if (char === "(") depth++;
      if (char === ")") {
        depth--;
        if (depth === 0) {
          results.push(text.slice(markerIndex + marker.length, index));
          break;
        }
      }
    }
    searchFrom = index + 1;
  }
  return results;
}

function collectBootstrapTools(warnings) {
  const text = fs.readFileSync(programPath, "utf8");
  const tools = [];
  const regex = /BuildBootstrapTool\(\s*"([^"]+)"\s*,\s*"([^"]*)"\s*,\s*"((?:[^"\\]|\\.)*)"/gs;
  for (const match of text.matchAll(regex)) {
    tools.push({
      name: normalizeToolName(match[1]),
      description: compactText(decodeCSharpStringLiteral(`"${match[3]}"`) || match[2]),
      source: path.relative(repoRoot, programPath).replace(/\\/g, "/"),
    });
  }

  if (tools.length === 0) {
    warnings.push("No bootstrap tools were discovered in Program.cs.");
  }
  return tools;
}

function collectMcpTools(warnings, errors) {
  const globalConstants = fs.existsSync(toolPackCatalogPath)
    ? collectStringConstants(fs.readFileSync(toolPackCatalogPath, "utf8"), "ToolPackCatalog")
    : new Map();
  const tools = [];
  for (const filePath of listFiles(toolsRoot, ".cs")) {
    const text = fs.readFileSync(filePath, "utf8");
    const constants = mergeConstants(globalConstants, collectStringConstants(text));
    for (const argsText of findMcpToolArguments(text)) {
      const positionalArgs = splitTopLevel(argsText, ",")
        .filter((arg) => !/^\s*[A-Za-z_][A-Za-z0-9_]*\s*=/.test(arg));
      const rawName = positionalArgs[0] ? evaluateStringExpression(positionalArgs[0], constants) : null;
      if (!rawName) {
        errors.push(`Could not resolve MCP tool name in ${path.relative(repoRoot, filePath)}: ${argsText.slice(0, 120)}`);
        continue;
      }

      const rawDescription = positionalArgs[1]
        ? evaluateStringExpression(positionalArgs[1], constants)
        : null;
      const rawTitle = positionalArgs[2]
        ? evaluateStringExpression(positionalArgs[2], constants)
        : null;
      if (!rawDescription) {
        warnings.push(`Using fallback description for ${rawName} from ${path.relative(repoRoot, filePath)}.`);
      }

      tools.push({
        name: normalizeToolName(rawName),
        description: compactText(rawDescription || rawTitle || rawName),
        source: path.relative(repoRoot, filePath).replace(/\\/g, "/"),
      });
    }
  }
  return tools;
}

function buildManifest() {
  const warnings = [];
  const errors = [];
  const plugin = readJson(pluginJsonPath);
  const mcpConfig = readJson(mcpJsonPath);
  const allTools = [
    ...collectBootstrapTools(warnings),
    ...collectMcpTools(warnings, errors),
  ];

  const toolsByName = new Map();
  for (const tool of allTools) {
    if (!tool.name) {
      errors.push(`Tool from ${tool.source} had an empty name.`);
      continue;
    }

    if (toolsByName.has(tool.name)) {
      warnings.push(`Merged duplicate discovery row for ${tool.name}.`);
      const existing = toolsByName.get(tool.name);
      if (tool.description.length > existing.description.length) {
        toolsByName.set(tool.name, tool);
      }
      continue;
    }

    toolsByName.set(tool.name, tool);
  }

  for (const requiredTool of requiredFacadeTools) {
    if (!toolsByName.has(requiredTool)) {
      errors.push(`Required facade tool missing from manifest: ${requiredTool}`);
    }
  }

  const tools = [...toolsByName.values()]
    .sort((left, right) => left.name.localeCompare(right.name))
    .map((tool) => ({
      name: tool.name,
      description: tool.description,
    }));

  const duplicateNames = tools
    .map((tool) => tool.name)
    .filter((name, index, names) => names.indexOf(name) !== index);
  if (duplicateNames.length > 0) {
    errors.push(`Duplicate tool names in generated manifest: ${[...new Set(duplicateNames)].join(", ")}`);
  }
  if (tools.length <= 21) {
    errors.push(`Generated manifest has only ${tools.length} tools; expected a static_all discovery hint, not foundation-only.`);
  }

  const serverConfig = mcpConfig.mcpServers?.unity_mcp_lens;
  const manifest = {
    manifest_version: "0.3",
    name: plugin.interface?.displayName || plugin.name,
    version: plugin.version,
    description: plugin.description,
    author: plugin.author,
    repository: plugin.repository,
    homepage: plugin.homepage,
    sourceOfTruth: "discovery_hint_only",
    executionSourceOfTruth: "Lens host tools/list and Unity bridge manifest",
    server: {
      type: "mcp",
      mcp_config: serverConfig,
    },
    tools,
  };

  return { manifest, warnings, errors };
}

function serializeManifest(manifest) {
  return `${JSON.stringify(manifest, null, 2)}\n`;
}

function main() {
  const { manifest, warnings, errors } = buildManifest();
  for (const warning of warnings) {
    console.warn(`[lens-manifest] warning: ${warning}`);
  }
  if (errors.length > 0) {
    for (const error of errors) {
      console.error(`[lens-manifest] error: ${error}`);
    }
    process.exit(1);
  }

  const serialized = serializeManifest(manifest);
  if (write) {
    fs.writeFileSync(manifestPath, serialized);
    console.log(`Wrote ${path.relative(repoRoot, manifestPath)} with ${manifest.tools.length} tools.`);
    return;
  }

  if (check) {
    if (!fs.existsSync(manifestPath)) {
      console.error(`Missing ${path.relative(repoRoot, manifestPath)}. Run node Tools~/Export-LensDevPluginManifest.js --write.`);
      process.exit(1);
    }

    const current = fs.readFileSync(manifestPath, "utf8");
    if (current !== serialized) {
      console.error(`${path.relative(repoRoot, manifestPath)} is stale. Run node Tools~/Export-LensDevPluginManifest.js --write.`);
      process.exit(1);
    }

    console.log(`${path.relative(repoRoot, manifestPath)} is up to date with ${manifest.tools.length} tools.`);
    return;
  }

  process.stdout.write(serialized);
}

main();
