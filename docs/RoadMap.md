# Roadmap

This project is evolving incrementally.

The goal is to improve structure and efficiency without slowing development.

---

## Current State

- Tool-centric architecture
- External agent control via MCP
- Working telemetry system
- Focus on token efficiency and low-noise tool exposure

---

## Near-Term

### 1. TSAM Refactor (Targeted)
- Refactor one large tool (e.g. GameObject management)
- Extract:
  - service logic
  - Unity adapter
  - typed models

### 2. Tool Output Standardization
- replace anonymous objects with typed models
- improve consistency for agents

### 3. Telemetry Improvements
- add call sequence tracking
- detect retry patterns
- measure output size more precisely

---

## Mid-Term

### 4. Shared Services
- reuse logic across tools
- reduce duplication
- simplify tool implementations

### 5. Reduced Scene Re-Scanning
- avoid repeated expensive queries
- explore caching or shared state

---

## Longer-Term (Optional)

### 6. Cleaner Separation from Unity APIs
- isolate Unity dependencies in adapters
- improve testability

### 7. More Structured Tool Surface
- shift from command-style tools to capability-style tools

---

## Guiding Principle

Do not rewrite the system.

Improve it by:
- refactoring where it hurts
- extracting reusable logic
- measuring real-world behavior

---

## Definition of Progress

Progress is:

- fewer retries
- smaller payloads
- clearer tool results
- less duplicated logic
- easier-to-reason-about behavior
