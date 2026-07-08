# Docs-walkthrough validation checklist (T019 / US3 / SC-005)

**Purpose**: Prove that an operator can follow the docs **alone** — the prebuilt
[`README.md`](../README.md) and the feature
[`quickstart.md`](../../../specs/006-prebuilt-image-per-login/quickstart.md) —
to launch either deployment option and bring up multiple per-login containers,
in under 15 minutes (SC-005). Step through sections A–D and record pass/fail
against SC-001..SC-006.

Record the start time before section A and the end time after section D to
confirm the < 15-minute target (SC-005).

- Start time: __________

## Section A — Build or pull the prebuilt image

- [ ] Followed only the docs (no reading source) to build **or** pull the image.
- [ ] Build fails fast if MT5 is unavailable (no incomplete image) — **SC-006**
      build invariant understood.

## Section B — Run one container (US1)

- [ ] Container ready in **< 60s** — **SC-001**.
- [ ] `docker logs` show **no** Python/VC++/pip/MT5 install/download lines —
      **SC-001 / FR-002**.
- [ ] `docker inspect -f '{{ len .Mounts }}'` reports **0** — **SC-003**.

## Section C — Run one container per login (US2)

- [ ] Launched two logins on two ports with `run-login.sh` (or `run-login.ps1`).
- [ ] Each endpoint serves its **own** account independently — **SC-002**.
- [ ] `docker rm -f` of one container leaves the other **still serving** —
      **SC-004**.
- [ ] Re-running with an **in-use host port** fails clearly (does not hijack the
      other endpoint) — edge case.

## Section D — Choose between options (US3)

- [ ] The docs make the bootstrap-vs-prebuilt trade-off clear enough to choose
      without reading source.
- [ ] Confirmed the bootstrap option (`deploy/wine-docker/`) still works and is
      unchanged — **FR-008**.

## Reproducibility spot-check

- [ ] Rebuilt from the same pinned inputs and obtained a functionally equivalent
      image — **SC-006**.

- End time: __________
- Total elapsed **< 15 min**? (SC-005)  ☐ yes  ☐ no

## Result

- Overall: ☐ PASS  ☐ FAIL
- Notes:
