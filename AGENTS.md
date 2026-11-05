# Purchase Memory — mandatory agent workflow

This file is the project memory for the root agent (Claude Code, Codex, etc.)
and its child agents. It applies to every following development stage unless
the owner explicitly changes the process.

## Data and secret safety (mandatory, no exceptions)

- `data/purchases.db` is the owner's real database. Do not open, read, copy or
  migrate it, and do not point tests, `dotnet ef`, app runs or any tool at it.
  Do not touch `data/receipts/` and `data/backups/`.
- The API migrates its database on start. An agent runs the API only with
  `Paths__DataDirectory` pointing to a fresh folder in the temp directory.
- After a stage that touches the backend, check that the modification time and
  size of `data/purchases.db` have not changed.
- The owner's receipt photos outside `test-data/` are read-only and are never
  copied into the repository.
- Git (since 2026-10-06): private repository `CHUKIN/purchase-memory`, branch
  `main`. Implementation and review agents use read-only git commands only
  (`status`, `diff`, `log`); the root agent commits and pushes after an
  accepted review, with no force-push and no history rewriting. Before a
  commit, check that `data/`, `logs/`, the owner's receipt photos and secrets
  are not in the index.
- Temporary files go only into the session temp directory, one subfolder per
  agent.
- Build and run .NET only via `./scripts/dotnet.sh`, the sidecar only via
  `./scripts/build-apple-sidecar.sh`.
- An agent never types in keys or tokens itself and never accepts them from
  chat. If the owner sends a key in chat, do not use it and ask them to revoke
  it. Keys live in `~/.zshrc` (e.g. `OPENAI_API_KEY`); run commands that need
  them via `zsh -ic '…'`. Never print, log or pass the value as an argument,
  never run `env`/`printenv`; only check presence or length.
- Paid calls (OpenAI, Anthropic, paid Ollama models, etc.) only with a budget
  named to the owner in advance.

## Sources of product decisions

Before planning a stage, check:

- `ПРОДУКТОВАЯ_СЕССИЯ.md` — confirmed requirements and product boundaries;
- `ДИЗАЙН_БРИФ_И_UI_СТЕК.md` — visual system and UI stages;
- `РЕЗУЛЬТАТЫ_ТЕХНИЧЕСКОГО_ЯДРА.md` — proven capabilities and known limits;
- `КОНТЕКСТ_ПРОЕКТА.md` — current architecture, decisions made and the map of
  documents (plans `ПЛАН_*.md`, stage results `РЕЗУЛЬТАТЫ_*.md`);
- the actual code and tests — documentation does not replace checking the
  implementation.

## When to start a stage

- A discussion, idea or question is not permission to change code.
- A new stage starts only after the owner explicitly agrees to implement it.
- Permission covers one agreed stage and does not allow moving on to the next
  one automatically.
- Exception (owner's decision 2026-10-17): the owner may approve several plan
  stages at once ("do 1.2, 1.6, 1.3 in a row"). The root agent then goes
  through them without stopping and stops only when the owner must choose
  (mockup, variant), on a blocker or a significant scope change, and before a
  deploy. A deploy always needs a separate "yes".

## Mandatory cycle of one stage

1. The root agent writes a bounded task brief: goal, scope, non-goals,
   acceptance criteria, required checks and the documents to update.
2. The root agent picks the model and reasoning effort itself and starts a
   separate implementation agent. The owner does not choose the model unless
   they ask to.
3. While the implementation agent works, the root agent does not read or check
   the affected code directly — this saves its context window for
   coordinating several stages in a row. No intermediate messages to the owner
   except the cases in "Communication with the owner".
4. After the implementation agent answers, the root agent starts a separate,
   independent review agent that took no part in the implementation. The
   review agent personally checks all changes (architecture, readability,
   accessibility, error handling, security, migrations, configuration,
   dependencies, no accidental changes) and runs the relevant build, unit,
   integration, frontend, e2e and visual checks to the extent the stage's risk
   requires. The review agent gets the task brief and the implementation
   agent's full report, nothing more — it must study the diff itself and
   re-verify facts, not retell someone else's report.
5. The review agent returns a compact verdict to the root agent: concrete check
   results (numbers, statuses), defects found (if any) and the stage's overall
   readiness. The root agent need not re-read the code to accept this verdict,
   but must read the verdict in full and not pass it off blindly as its own
   conclusion.
6. If the review agent found defects, the root agent sends the implementation
   agent a concrete follow-up listing the fixes in the review agent's words.
   After the fixes the review agent (same or new) re-checks. A stage cannot be
   considered ready based only on the implementation agent's report without a
   separate review agent.
7. After a successful check the root agent updates the plans and the stage
   result file: what was done, what was checked, known limits and the next
   candidate step.
8. Before the next stage the root agent stops, shows the owner the result or
   how to open it, explains the decisions made and asks only the necessary
   questions. Continuing needs a new explicit consent, unless the owner
   approved several stages in advance (see "When to start a stage").

For truly independent subtasks the root agent may run several agents in
parallel, but must split file ownership in advance and do the final
integration review itself.

## Check levels by risk (owner's decision 2026-10-17)

The root agent names the stage level in the task brief. When in doubt, go one
level higher.

- **Light** — texts, translations, help, small screen tweaks without new
  logic. One implementer; the review agent reads the diff (may be as strong as
  the implementer); run typecheck, frontend tests and the build.
- **Normal** — screens, new buttons and navigation, endpoints without
  migrations, fixes. Implementer + independent review agent. The implementer
  runs only the tests of its area; the full set (backend + frontend + build)
  is run once by the review agent before merging.
- **Full** — database and migrations, sign-in and access, deploy and
  infrastructure, money, receipt recognition. The whole cycle above with no
  shortcuts: full set on SQLite and on a temporary Postgres, migration Down/Up
  check, Opus implementer and Opus review. Real bypass attempts belong to the
  pre-deploy check (below), not to the stage review.

Saving limits (owner's decision 2026-10-29; overrides the rules above on
conflict):

- Light and Normal level: implementer is Sonnet; review is Sonnet (Opus only
  for real risk), one review: reads the diff and runs the full test set once.
  No screenshots, Storybook builds or e2e unless the stage is about how a
  screen looks and cannot do without a picture.
- Opus and the full cycle only for the "Full" level.
- Minor (low/cosmetic) review findings do not start a new round: they go to
  `ЧТО_ДАЛЬШЕ.md` as backlog. A fix round is only for real bugs; after it the
  root agent looks at the diff itself and runs the tests once, no second
  review agent.
- The implementer runs only the tests of its area; the full run happens once.
- The root agent draws simple mockups itself, without agents.

Tests and builds (owner's decision 2026-10-17, softens the rule "do not run
checks after changes, ask me"): inside an approved stage agents run tests,
typecheck and builds themselves to the extent of their level. Paid calls and
anything that touches prod only with the owner's consent.

Independent stages may run in parallel in separate worktrees; the shared
integration review and the full test set run once after merging.

Deploy: several finished stages may be accumulated and deployed as a batch
after `scripts/smoke-deploy.sh` (see `ЧТО_ДАЛЬШЕ.md`). A stage with a database
migration is deployed on its own, with a prod database backup before it.

Pre-deploy security check (owner's decision 2026-11-05): the stage review
checks protection by reading code and tests; real bypass attempts happen once
for the whole batch before deploy. The root agent starts one Opus agent on the
diff from the last deployed version to `main`: API on temporary data, requests
without a token, with a forged and an expired token, to other users' ids, path
traversal, large and fake files, limits, outbound requests (SSRF), plus
network/proxy for stages about sign-in and deploy. No deploy of a batch
without this check; if the batch has no API, sign-in or access changes, a
smoke check after deploy is enough.

Stage result file — short: what was done, check numbers, limits, next step (no
retelling of the code).

## Lessons from past stages

- Accept changes to receipt recognition (prompts, schema, models, pipeline)
  only by a measurement on labelled data
  (`test-data/receipts/household-2026-08/results/ground_truth_items.json`,
  `score_items.py`) with before/after numbers.
- An agent does not hand in its report until its background tasks have
  finished; the root agent checks this before starting the review.
- For stages about sign-in, access, network and deploy, protection is checked
  with real bypass attempts (requests without a token, with a forged token,
  from the network/through a proxy), not only by reading code — once per
  batch, in the pre-deploy check.
- Each stage's result is a file `РЕЗУЛЬТАТЫ_<STAGE>.md`.
- Language (owner's decisions 2026-10-06 and 2026-11-05): agent instructions
  (`AGENTS.md`), plans (`ПЛАН_*.md`, `ЧТО_ДАЛЬШЕ.md`), result files and other
  working notes for agents are in short English. Messages to the owner in chat
  and documents the owner asked to have written for themselves (guides,
  explanations) are in plain, simple Russian. Do not translate other existing
  Russian files.
- `CapturePage.test.tsx › keeps the existing review…` is known to be flaky: a
  single failure is re-checked with a second run before treating it as a
  regression.
- The help section in the app («Подсказки», `/help`) is part of the product.
  Every stage that adds or changes user-visible behaviour must include a help
  update in its task brief and acceptance criteria: topic texts in both
  languages — `src/PurchaseMemory.Web/src/features/help/helpText.ts` (`helpRu`
  and `helpEn`), topic structure and links into the app —
  `src/PurchaseMemory.Web/src/features/help/topics.ts`, the on-screen «Как это
  работает» link — the `HelpLink` component. Button names in the help must
  match `i18n.ts`. The review agent compares the help with the real behaviour
  and UI, not with the report; `Help.test.tsx` checks only the structure
  (ru/en keys, existing topics).

## Model choice

Split by level (owner's decision 2026-10-13; for Claude Code: Opus / Sonnet /
Haiku, for other tools their equivalents by strength):

- **Senior model (Opus) — root agent.** Plan, task brief, decisions,
  coordination, integration, final review. It is also implementer and review
  agent for risky stages: sign-in and access, deploy and infrastructure,
  migrations and data, receipt recognition, large end-to-end rewrites (e.g. a
  UI stack change).
- **Middle model (Sonnet) — default implementer** for normal stages: screens,
  endpoints, fixes, tests. May be the review agent for simple stages with no
  risk to data or access.
- **Fast model (Haiku) — mechanics only:** gathering facts and searching code,
  renames, texts and translations, updating documents, running checks. Do not
  give it refactoring that changes logic and do not make it a review agent.
- Escalation: if a middle-model stage fails review twice, implementation moves
  to the senior model.
- The review agent is never weaker than the stage's implementer.
- Speed and cost must not lower a stage's quality. When in doubt, pick a
  stronger implementer and shrink the task scope.

## Communication with the owner

- By default work quietly and come back with one final message: result, short
  summary, questions and the needed information.
- Do not publish reasoning, internal details and intermediate statuses unless
  they help the owner decide or affect the work.
- Mention starting a separate agent only when active system rules require it
  or when it matters to the owner to know about the delegation.
- Contact the owner mid-way only on a blocker, a needed approval/permission, a
  significant scope change, or a really long operation where without a status
  it is unclear whether work continues.
- If active system rules require more frequent updates, follow them, but keep
  messages as short as possible and do not reveal internal reasoning.
- Do not pass off the implementation agent's report as a check: only a
  separate review agent confirms a stage is ready.
- At the end of a stage report: result, changed areas, tests run, how to
  open/check it, known limits and questions before continuing.

## Implementation quality

- Use the latest stable versions of .NET, C#, React, TypeScript and libraries
  at the time of the stage; pin exact versions in lock files.
- Write human-readable code with clean boundaries. Comments explain a
  non-obvious decision or constraint, not the syntax.
- Hide external AI providers, file/object storage and infrastructure
  mechanisms behind application contracts where swapping is really needed.
- No secrets in the frontend, sources or committed configuration.
- Keep file logging and add structured context without secrets or full user
  images in the log.
- Preserve existing user changes and do not run destructive git/file
  operations without separate permission.
- Do not complicate the first version with `later` features unless they are
  needed for the agreed stage or a safe evolution of the architecture.

## Special rule for UI-0

Stage `UI-0` ends only after a Storybook walkthrough on Purchase Memory
components. The root agent shows the owner on real examples how styles and
providers are wired, how stories and Controls create states, how locale and
viewport change, and how an approved variant gets into the app.
