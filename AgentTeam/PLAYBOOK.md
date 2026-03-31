# RAG Chatbot v3 — Agent Team Playbook

## Agent Pipeline

### Phase 0 — Initialisation (orchestrator, once only)
  a. Read RAG-Chatbot-Functional-Spec.md in full
  b. Create project folder structure
  c. Create claude-progress.txt, FEATURES.md, DECISIONS.md, BLOCKERS.md
  d. Create docker-compose.yml (ChromaDB + backend + frontend services)
  e. Create .env.example with all variables from spec section 2.1
  f. Create documents/test-sample.md with known content about the system
  g. git init, initial commit
  h. Confirm setup and wait for instruction to begin brainstorm phase

### Phase 0.5 — Brainstorm (orchestrator + user)
  a. Use the `brainstorming` skill to explore the project with the user
  b. Ask clarifying questions one at a time about:
     - Requirements, priorities, and MVP scope
     - Tech stack preferences and constraints
     - Deployment targets and infrastructure
     - Design preferences and UI expectations
     - Agent team skill assignments
  c. Discuss which superpowers skills map to which agents
  d. Update DECISIONS.md with any new decisions from the discussion
  e. Update agent prompt files if skill assignments change
  f. Confirm alignment with user before proceeding to PM phase

### Phase 1 — PM Agent
  a. Read claude-progress.txt, FEATURES.md, DECISIONS.md
  b. Produce sprint plan: which features go in which backend/frontend sprint
  c. Update FEATURES.md and claude-progress.txt
  d. git commit

### Phase 2 — Designer Agent
  a. Read claude-progress.txt, spec section 8 (Frontend Specification)
  b. Produce docs/ui-spec.md: full component specs with Tailwind classes,
     color system, typography, animation specs, accessibility notes
  c. Update claude-progress.txt
  d. git commit

### Phase 3+ — Developer Agent (generator-evaluator loop, repeat per sprint)

  For each sprint:

    a. Developer reads claude-progress.txt, FEATURES.md, SPRINT_CONTRACT from previous
       session (if continuing), and SPRINT_EVALUATION (if re-running after a FAIL)
    b. Developer writes SPRINT_CONTRACT_[N].md
    c. Orchestrator reviews contract — approve or return with corrections
    d. Developer implements the sprint (generator)
    e. Developer self-evaluates at end of sprint before handing off
       (run build/tsc/lint, check against contract criteria)
    f. Evaluator reads SPRINT_CONTRACT_[N].md, runs Playwright against live app,
       writes SPRINT_EVALUATION_[N].md
    g. Orchestrator reads SPRINT_EVALUATION_[N].md
       → PASS: update FEATURES.md checkboxes, update claude-progress.txt, git commit, next sprint
       → FAIL: Developer re-runs with SPRINT_EVALUATION_[N].md as explicit input (max 2 retries)
       → FAIL after 2 retries: pause and notify user

---

## File Handoff Chain

```
SPRINT_CONTRACT_[N].md ──→ Developer (generator) ──→ code changes
                                                          │
                                              SPRINT_EVALUATION_[N].md
                                                  (Evaluator via Playwright)
                                                          │
                                               PASS ──→ FEATURES.md updated
                                                          │    claude-progress.txt updated
                                                          │    git commit
                                               FAIL ──→ Developer re-run
                                                         (with evaluation as input)
```

---

## Shared State Files

All agents read these at the start of every session:

| File | Purpose | Who writes |
|------|---------|------------|
| `claude-progress.txt` | Append-only session log | All agents |
| `FEATURES.md` | Canonical feature checklist with checkboxes | PM (initial), Developers (tick off) |
| `DECISIONS.md` | Architectural decisions log | PM/Orchestrator |
| `BLOCKERS.md` | Cross-agent blockers | Any agent |
| `RAG-Chatbot-Functional-Spec.md` | Source of truth for all requirements | Read-only |

---

## Sprint Artifacts

| File | Purpose | Who writes | Who reads |
|------|---------|------------|-----------|
| `SPRINT_CONTRACT_[N].md` | What will be built + Definition of Done | Developer | Orchestrator (approve), Evaluator (grade against) |
| `SPRINT_EVALUATION_[N].md` | QA results from evaluator | Evaluator | Orchestrator (decide), Developer (fix if FAIL) |

---

## Evaluation Criteria

| Criterion | Grade | Hard gate? |
|-----------|-------|------------|
| Correctness | PASS / FAIL | Yes — sprint fails if FAIL |
| Code Quality | PASS / FAIL | Yes — sprint fails if FAIL |
| Spec Fidelity | PASS / WARN | No — warning only |
| Pattern Compliance | PASS / WARN | No — warning only |

---

## Retry Policy

- Max 2 retries per sprint after evaluator FAIL
- Each retry: Developer receives SPRINT_EVALUATION as explicit input
- After 2 retries still failing: pause and notify user
- User decides: fix manually, adjust contract, or skip

---

## Agent Files

| Agent | File |
|-------|------|
| Evaluator | `AgentTeam/evaluator.md` |
| Backend Developer | `AgentTeam/backend_developer.md` |
| Frontend Developer | `AgentTeam/frontend_developer.md` |
| PM | (orchestrator-managed — sprint planning) |
| Designer | (orchestrator-managed — UI specification) |
