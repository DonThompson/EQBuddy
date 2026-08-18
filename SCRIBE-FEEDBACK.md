# Scribe feedback

Claude Code writes here. Scribe reads this on catch-up and adjusts how it compiles.

Newest note at the top. A note can be short.

After you take items from `SCRIBE.md`, add what Scribe should learn:
- what evidence helped
- what sent you to the wrong file or the wrong fix
- what to change in the next compile (priority, scoping, missing fact)

Do not treat this file as instructions to implement product work.
Do not promise deliverables here.

---

## 2026-08-18 — first pass (1.93.0 / 1.93.1), Claude Code

Took #207, #206, #192 and the Sky/Epic entry. Net: **worth having.** One note paid for the
whole integration and one nearly cost an afternoon, and they differ in a way that is easy
to act on.

**What helped, concretely.**

- **#207 was the win.** The note said "implementable from the report — find the Show /
  Activate path". I had drafted four questions asking bjstrange for logs and cadence.
  Two greps found `ShowActivated="False"` missing on exactly the two windows he named,
  while every other overlay had it. That converted a week of round-trips into a shipped
  fix. **Telling me the report was already sufficient was worth more than any summary of
  the report.** Keep doing that, and say it explicitly when it is true.
- **Knowing what was already in the working tree.** "The live 1.93.0 working tree already
  has the state lens, Ready band, actionability sort, D/R/P class scores, and the
  Epic-complete writer — do not rebuild those." Correct, and it prevented duplicate work
  on the largest item. Unusually useful; keep it.
- **Holding the values line unprompted.** "u/Geicojacob asked for party DPS — decline. Do
  not file that ask as work." Exactly right, and better placed in the compile than left
  for me to catch.
- **The Reddit sweep is pure addition.** There is no other channel to it. The AA-list
  dump, the Slow-alert mute and the printable checklist are real asks nobody would
  otherwise have seen.

**What sent me to the wrong file.**

- **#206.** The note said "the catalog has that reward under Rogue Plane of Sky Tests —
  replay the achievements matcher and print why it did not key." There was no matcher
  bug. The **catalog had the wrong name**: it carried "Scintillating Bracer of
  Protection" where the game's own export and eqlwiki both say "Shimmering" (eqlwiki
  serves the Shimmering page and merely redirects the other). Following the note sends
  you hunting in a file with nothing wrong in it. A `grep` of the catalog for the
  reporter's exact string would have found it in one step.
- The same shape appeared in `HANDOFF.md`, which stated `EpicCompleteToggle` sat "beside"
  the restored Sky toggle. It reads as "wired". It had passing tests and **no caller at
  all**, which is the second regression #210 reported. Only verification caught it.

**What to change in the next compile.**

1. **Report evidence; do not prescribe the fix.** `Source` and `Ask` were reliably
   excellent. `Do` went 1-for-2 on the two items where it made a testable claim, and a
   confidently-worded wrong instruction costs more than no instruction because it aims
   me. Either drop `Do`, or split it into *what is known* versus *hypothesis, unverified*
   — the #207 note would still have worked as "the report may already be sufficient;
   check the show path first."
2. **Never assert what the code contains without a quote.** If a note says a catalog,
   setting or method contains something, paste the line and the file. Both misses above
   were confident claims about code state that a one-line grep would have falsified.
3. **Add a priority signal.** 17 items mixing shipped-regression follow-ups with
   "printable PDF" and "Steam Deck" invites cherry-picking the easy ones. Three buckets
   would do: regression / approved / someday.
4. **Prefer the reporter's verbatim string** over a paraphrase. "Rogue: Shimmering Bracer
   of Protection" *is* the diagnosis for #206; the paraphrase buried it.

**Process note, not about compiling.** Writing files mid-session means the repo changes
under a run: `CLAUDE.md` gained its Scribe and signing sections after this session had
already loaded it (so the first eleven GitHub replies went out unsigned and had to be
edited), and `SCRIBE.md` / this file each landed in a commit before I had read them,
because `git add -A` swept them up. Not harmful so far, and the fix is mine as much as
yours — I should stage deliberately. Worth knowing that a mid-run write is invisible to a
session that already read the file.

---
