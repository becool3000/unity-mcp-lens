const assert = require("assert");
const path = require("path");

const batch = require(path.resolve(
  __dirname,
  "..",
  ".agents",
  "plugins",
  "lens-dev-plugin",
  "skills",
  "unity-mcp-bridge",
  "scripts",
  "Invoke-UnityMcpBatch.js"));

const single = batch.parseStepsJson(JSON.stringify({
  name: "health",
  tool: "Unity.Editor.HealthCheckFast",
  arguments: { includeCandidates: true },
}), "single-object-test");

assert(Array.isArray(single), "single object should be wrapped into an array");
assert.strictEqual(single.length, 1, "single object should produce one step");
assert.strictEqual(single[0].tool, "Unity.Editor.HealthCheckFast");
assert.strictEqual(single[0].arguments.includeCandidates, true);

const many = batch.parseStepsJson(JSON.stringify([
  { tool: "Unity.Editor.HealthCheckFast" },
  { tool: "Unity.Bridge.ListConnections" },
]), "array-test");

assert(Array.isArray(many), "array input should remain an array");
assert.strictEqual(many.length, 2, "array input should preserve all steps");

const normalized = batch.normalizeStep(single[0], 0, 45);
assert.strictEqual(normalized.name, "health");
assert.strictEqual(normalized.tool, "Unity.Editor.HealthCheckFast");
assert.strictEqual(normalized.timeoutSeconds, 45);

assert.throws(
  () => batch.normalizeStep({}, 0, 45),
  /requires a string 'tool'/,
  "invalid step should still be rejected");

console.log("Invoke-UnityMcpBatch parsing tests passed.");
