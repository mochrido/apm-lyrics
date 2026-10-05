# APM Lyrics: controller rulings and deferred minors

This file is the durable record of every decision taken on the user's behalf during the
subagent-driven implementation of APM Lyrics. The workspace that held the ledger is deleted
at the end of the run, so this file (and git history) is the record. Generated from
.superpowers/sdd/2026-10-05-apm-lyrics/progress.md before deletion.

Each ruling states what was decided, why, and what it costs if wrong. The user reads this
and reworks whatever was decided incorrectly.

---

## Rulings (chronological)

  Ruling: installed Microsoft.DotNet.SDK.8 (8.0.425) via winget myself rather than
  letting a subagent do it - machine-level install is outside the worktree and the
  skill's stop-condition class. Cost if wrong: an unneeded SDK on the machine,
  trivially uninstalled.

| 1 | T6 -> T7 | T6's code declares `CatalogClient : ICatalogClient` and `AppleLyricsCache : IAppleLyricsCache`, but T7 declared those interfaces | CONFLICT: T6 cannot compile; it references types first declared in T7 | Ruling: moved both interface declarations into T6 (where their implementers live), removed them from T7, updated T6 and T7 Produces blocks. Cost if wrong: interface location differs from the spec's section 3 sketch, which is cosmetic. |

| 6 | T1 internal | T1 set `OutputType=WinExe` with no entry point until T10 | CONFLICT: T1's build would fail (no Main) | Ruling: T1 sets `OutputType=Library`; T10 flips it to `WinExe` as an explicit step. Also added `UseWPF` to the test csproj (references a WPF assembly). Cost if wrong: none; a library builds and tests fine, and the flip is a one-line step. |

| 8 | T11 internal | Installer referenced `SetupIconFile=app.ico` that no task creates | CONFLICT: installer build fails on a missing file | Ruling: removed the `SetupIconFile` line. Cost if wrong: installer ships without a custom icon, cosmetic and fixable later. |

  not enforceable here. Ruling: accept the inheritance rather than fake the parameter.
  Cost if wrong: higher cost and slower iteration than the guide intends; no impact on
  correctness of the artifacts. Compensating: dispatch scope stays narrow (one task, one
  brief) so no child carries unnecessary context.

  file (progress.md) instead of creating the report file at the dispatched path. Ruling:
  the controller filed the report verbatim at the correct path (task-1-report.md) from the
  child's final message, and every later dispatch states the report path and "do not write
  to progress.md" explicitly. Cost if wrong: the report is controller-filed rather than
  author-filed; the content is verbatim and the reviewer sees it either way.

  cannot compile (CS0246). The implementer added the directive. Ruling: the deviation
  stands; the plan text is defective here and matches the convention of all seven later
  test files, which each declare `using Xunit;`. Cost if wrong: one redundant directive.

- Task 1: Ruling: the brief's SmokeTests.cs does not compile as written - the brief's test
  csproj drops the template's `<Using Include="Xunit" />` global using while the smoke test
  carries no using directive, so `[Fact]` is unresolved (CS0246, watched fail). Added
  `using Xunit;` to SmokeTests.cs, matching every later test file in the plan (all seven
  declare it explicitly). Cost if wrong: none; one directive the later files already carry.

  Ruling: controller fixed the plan text (commit a159d9c) with synthetic fixtures and
  added the missing empty-line test, then dispatched a fix round for the test file.
  Cost if wrong: none; the plan now matches its own constraint.

  Ruling: history rewrite is a destructive public operation, which is one of the four
  stop conditions; asked the human partner to choose. See "Human decisions" below.

  Ruling: counted the block myself (10 [Fact], 0 Theory, 0 InlineData) and checked it
  against Task 4's Review Focus obligations (artist album suffix, duration collisions,
  the unresolvable-file tier, staleness, no-match-null). All are covered by the ten.
  The number was stale, not the test list, so I corrected the plan to 10 (commit 03f40be)
  rather than inventing an eleventh test to satisfy a wrong count. Cost if wrong: a test
  the plan intended is absent; the obligations are nonetheless all exercised, and the
  final review sees this note.

  Ruling: both deviations stand. The brief was self-contradictory and the added tests
  cover declared-but-untested interface. NOT updating the plan now, deliberately: the
  reviewer must see the code as written and judge the deviation itself, rather than
  having the record retro-edited to match. Plan reconciliation happens after the review
  verdict. Cost if wrong: the plan and the shipped code disagree until reconciled, which
  the final review would surface anyway.

  Ruling: both are plan defects, not implementer errors; corrected in the plan and in the
  Task 8/10 code blocks (commit e60f40c). Cost if wrong: the progress rule is a small
  visual element and the dispatcher hops add one queued operation per SMTC tick at 4Hz;
  neither is load-bearing to correctness, and both are visible in review.

  Ruling: this is a weak test, not a defective matcher. It is a plan-mandated test (the
  brief's text is the source), so per the rubric it is reported as a finding and the
  reviewer must judge its severity; the controller does not pre-judge it. Cost if wrong:
  the tiebreak could regress silently and the suite would stay green; the final review and
  the deferred-minor list carry it.

  Ruling: the plan text is at fault, not the implementer (the brief mandated both test
  bodies verbatim, and the implementer transcribed them faithfully). Spec 4.2 states the
  title key and the duration tiebreak as requirements, so a suite that cannot fail on
  either is a real gap regardless of the matcher happening to be correct today. Fixed the
  plan test bodies (commit 7af6638: reversed input order for the first; made the
  non-matching title the duration-nearest for the second) and dispatched a test-only fix
  round that must PROVE each fix is load-bearing by mutating the matcher and watching the
  test fail, then restoring. Cost if wrong: the two tests change shape but the matcher does
  not; the mutation evidence in the fix report shows whether the fix actually bites.

  Ruling: corrected the entry in place rather than deleting it, so the deviation is
  visible. Future dispatches state the prohibition as a hard stop with the reason.
  Cost if wrong: an inaccurate provenance line in a scratch ledger; the git history was
  never affected.

  Ruling: the deviation stands; the plan text is defective here, same class as the Task 1
  ruling. Cost if wrong: two redundant directives the compiler would have demanded anyway.
  (2) Mutation check (controller-run, not self-reported): neutralising the catch and the
      BackdropOpacity clamp turns exactly 2 tests red (corrupt-file, opacity-range) and
      leaves 4 green, then restores to 41/41. Both guards are load-bearing.

  Ruling: corrected in place rather than reverted, so the violation stays visible. Cost if
  wrong: a scratch file overstates how much of Task 5 was verified by whom; the committed
  code and its tests are unaffected, and git history was never touched.

  Ruling: the plan was defective, not the implementers; three tasks would have hit it
  independently. Cost if wrong: redundant using directives the compiler demands anyway.

  Ruling: fixed in the plan BEFORE Task 7 ships (commit c5a8ec6) by setting Dan's catalog
  duration to 306.0 so it becomes the nearer body match and only the title key can produce
  the expected value. A comment explains why the real measurement was perturbed, so the
  spec's recorded numbers stay authoritative and the divergence is visible. Same commit
  corrected a comment left stale by the Task 4 fixture change, in both the plan and the
  committed test file, restoring them to byte-identical (sha256 fb8861e4...). Cost if
  wrong: Task 7's fixture no longer reproduces the spec's exact pair, which the comment
  states explicitly; the spec table remains the record of the real measurements.

  plan never specified and without which 4 tests fail. Ruling: accept pending review; the
  plan's Task 6 file list should gain that csproj note once the review confirms it.

  id. Ruling: the implementer is right and the controller was wrong; its fix is the
  load-bearing one. Cost if wrong: none; the arithmetic is checkable and the plan now
  documents which field matters.

  Ruling: never script a structural rewrite of the plan; apply targeted patches and check
  fence parity after every plan edit. Cost if wrong: none now; the check is cheap and the
  failure mode is loud (task-brief refuses).

  Ruling: DEFERRED, not implemented. It is a pure optimisation: the title+artist tier
  yields the same lyrics for every case, so the user-visible behaviour is identical and
  the cost is one catalog lookup per candidate (already disk-cached and offline-capable).
  Recorded for the final review rather than adding a task late for a performance-only gain.
  Cost if wrong: marginally slower resolution on cache misses; no correctness impact.

      Ruling: correct resolution; the alternative (deleting the call) would have dropped a
      spec-required feature (spec 6's minimum line count). Cost if wrong: a Task-8 commit
      touches a Task-5 file, which the Task 8 review has been asked to inspect.
  (2) `if (Width <= 0)` was a silent no-op: a fresh WPF Window reports NaN and NaN <= 0 is
      false, so a never-explicitly-sized window silently kept none of the saved size.
      Measured 640x200 after the fix. Third instance of the silent-no-op family in this
      block, and the implementer reported it rather than quietly patching.
  It also reported spec 5.2's crossfade as unowned rather than silently adding or dropping
  it - the right call, and it is what prompted the fix below.

      window. Ruling: the SPEC was loosely worded, not the code. Corrected the spec's
      justification (commit 15ab8b8) to name CanResize and explain that native resize
      comes from WS_THICKFRAME. Cost if wrong: wording only; behaviour is identical.
  (b) Deviation provenance: a Task 8 commit modified AppSettings.cs, owned by the closed
      Task 5. Ruling: correct and necessary. Root cause was the controller's 3f7f80b,
      which added NeighbourRadius to the plan DOCUMENT only, so the brief referenced a
      field no build had; deleting the Render() call instead would have silently dropped
      spec 6's minimum line count. The reviewer traced the 0-4 clamp as load-bearing
      (radius 2 at lineCount 3 would index out of range without it). Ledgered as a
      process finding, not an implementer fault.

      Task 5. Ruling: correct and necessary. Root cause was the controller's 3f7f80b,
      which added NeighbourRadius to the plan DOCUMENT only, so the brief referenced a
      field no build had; deleting the Render() call instead would have silently dropped
      spec 6's minimum line count. The reviewer traced the 0-4 clamp as load-bearing
      (radius 2 at lineCount 3 would index out of range without it). Ledgered as a
      process finding, not an implementer fault.

  Ruling: REAL GAP, sent to a fix round. Verified by the controller: AppSettings already
  carries all five missing fields and OverlayWindow.ApplySettings already consumes all
  five, so this is UI-only work with no model or overlay change. Plan corrected in
  b818849 (five controls added, live apply wired to every control, and the Apply button
  replaced by Close because with live apply a confirmation button is a dead control,
  antislop R-26).
  Cost if wrong: five more controls than before, all backed by fields the overlay already
  honours; the manual checklist exercises them.

  Ruling: both corrections accepted; they are repairs of controller defects, not deviations
  from intent. Plan synced to the shipped forms in bf93475 (surgical block swap; LF
  restored; fences 108; all 11 tasks still extractable). Cost if wrong: the colour field
  rejects partial input rather than accepting it, which is the only safe option given every
  partial string throws; the manual checklist exercises the colour fields.

  findings table and no verdict line. Ruling: NOT a usable review. Rather than spend a
  second child re-reviewing, the controller verified its two substantive discoveries
  directly (the mis-anchor above, and the stale brief below) and completed the third
  investigation it had started but not finished (the clipping measurement below).
  Process note: this is the second child in the run to return raw reasoning instead of a
  verdict; the hardening is one deliverable per dispatch plus an explicit verdict line.

  before measuring it. Ruling: real regression, fix round 2 dispatched (agent
  sa-0-a0cbe412) to wrap the StackPanel in a ScrollViewer, which also makes
  RequestBringIntoView work so keyboard focus scrolls controls into sight.
  Cost if wrong: the window gains a scrollbar rail only when content overflows; at the
  default size nothing further changes.

  editing (worktree shows it modified). Ruling: do NOT sync the plan to uncommitted work;
  re-extract that block only after the round-2 commit lands, then re-run the audit to
  confirm 23/23.

- END-OF-RUN RULING (merge + push): the feature branch is NOT merged and NOT pushed, and
  neither happens in this run. The SDD skill's own stop list names a merge and a push to a
  shared branch as side effects to ask about first, and the user's standing instruction
  covers the 12 tasks plus the final review, not the merge. The repo is mochrido/apm-lyrics
  (public, sole collaborator is the user) and its main currently holds spec + plan only.
  Cost if wrong: the user merges a branch that was ready, or pushes it themselves; nothing
  is lost because the branch and its history are local and intact.

    Ruling: real spec violation, remove it. The installer may not offer a capability the
    spec defers and the user excluded, even unchecked-by-default, because the checkbox is
    the only thing that would create it. Cost if wrong: a user who wanted autostart must
    make a Startup shortcut by hand.
  * MISSED REVIEW: Task 11's review was never dispatched (tasks 1-10 all had one). Package
    review-bf93475..20f4804.diff generated and the review dispatched now.

  is not an implementer fault. Ruling: fold the README refresh into the FINAL fix wave
  alongside the installer autostart removal, so both get one review pass rather than two,
  and so the README's build instructions can reflect the final committed state.
  Cost if wrong: the front page understates progress for one more review cycle.

  Ruling: real, enters the loop as Task 9 round 3. Cheapest correct fix is to separate
  "refresh text" from "animate" so ApplySettings re-renders without the fade.
  Cost if wrong: styling edits no longer animate, which is the intended reading of the spec.

  person expects a settings file to be editable. Ruling: real; folded into Task 9 round 3
  as a second finding, fixed in Sanitize (normalize the three colour strings) plus a
  tolerant parse in ApplySettings so no caller can crash the overlay.
  Cost if wrong: a corrupt colour silently becomes the default instead of crashing.

  as this is written. Ruling: sync that block only after the fixer reports, never against
  an in-flight write. Cost if wrong: one more sync commit.

  Ruling: the plan is what a future re-dispatch would read, so a stale mirror is a real
  (if invisible) defect; the wider audit convention replaces the narrow one from here on.
  Cost if wrong: none to shipped code; the plan is documentation.
