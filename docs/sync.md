# How the phone and the server stay in step

The app works offline: every save goes to the phone's own database and into a queue (the outbox). When there is signal, a **sync** sends the queue, then fetches what other workers saved. The code is `frontend/src/sync/sync.ts`; the endpoints are in [api.md](api.md).

## One sync, in order

1. **Send** each queued change, oldest first, as `PUT /api/<thing>/<id>`. Sending a change twice (a lost reply) is harmless.
2. **Upload files** (consent recordings, photos) with the 5-minute links the API gives.
3. **Upload the audit trail** (who saw or changed what, kept even offline).
4. **Pull** patients, referrals and readings that changed since this phone last asked (a nurse's answer to a referral, a colleague's patient).

If the network drops at any point, the sync stops quietly and the next one carries on.

## Versions

Every record has a `version`. The phone sends the version it last saw. If it still matches, the server saves, adds 1, and answers with the saved record, which the phone keeps. So a second edit is built on the new version, even when two edits were queued before the first reached the server. If it does not match, someone else changed the record first: that is a **conflict** (`409`, with the server's copy).

## What happens on a conflict

| Record | Rule | Why |
|---|---|---|
| **Patient** | **A person reviews it.** Nothing is overwritten. The phone keeps its change and the server's copy side by side. The Sync page shows which fields differ and offers **Keep mine** (sent again on top of the server's version) or **Use the server's** (this phone's change is dropped). | Patient details are the one place where both sides may be right, and a wrong silent choice could lose a correction. The choice is audited. |
| **Appointment / referral** | **The server's copy wins.** The phone's change is dropped and replaced by the server's copy. | A nurse's answer to a referral must not be undone by an older edit made on a phone that had not seen it. |
| **Reading (vitals)** | **The server's copy wins.** | A reading is never edited. A conflict means a bug or a reused id, not a real disagreement. |

Each outcome writes an audit entry ("conflict reviewed: this phone's change was kept" or "the server's copy was kept"). Until a patient conflict is reviewed the change stays in the queue, marked `conflict`, and other workers' newer copy is not pulled over it. If the server's reply cannot be read, the change also stays in the queue, marked `conflict`.

The wording on the review card is English in every language until native speakers translate it (the other strings there are drafts too).

## What the pull leaves alone

A record that still has a change waiting in the queue is not overwritten by the pull: its own save, and the rules above, come first. A copy on the phone with a higher version than the server's is kept.

## Known limits

- The pull remembers the time of the last record it received. Two records changed at the exact same instant right at a 500-record page boundary could be skipped until they change again; this does not happen at the scale of this project.
- Deleting is not possible by design: a wrong reading is marked `entered-in-error`, as FHIR does.
