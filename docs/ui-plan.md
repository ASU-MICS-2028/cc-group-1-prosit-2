# HealthConnect UI plan

**Owner:** Tijani (frontend) · **Design source:** HealthCheck Figma kit (ADR 0002) · **Deadline:** feature freeze Mon 2 Nov, noon

Each row maps a Figma frame to a HealthConnect screen. The brief's four weeks are squeezed into one, so anything past the MVP is a mock or documented future work.

## Built (PR #2, then the UI flows PR)

| HealthConnect screen | Figma frame | Notes |
|---|---|---|
| Sign-in (demo roles) | Login | Real sign-in comes with Bernard's `/auth/login` |
| Home | Home Screen | Worker card, "refer today" alert, numbers, sync |
| Patients | Mis Contactos | Search, monogram cards, floating + |
| Register a patient (3 steps) | Añadir Contacto 1–3 | Consent → about the patient → where they live |
| Patient record | Cards | Details, vitals history with flags, visits |
| Record vitals | – | Live "refer today" prompt |
| Book visit / referral | Añadir Contacto | Type, time, facility, priority |
| Visits | – | Upcoming, soonest first |
| Sync, Profile | Mi Perfil | Queue, language, sign out |
| **Registered!** (after saving) | Felicidades | Kit illustration; "Record vitals now" or Done |
| **Sex and consent as picture tiles** | Indica tu Género | One tap; native radios underneath |
| **Weight and height wheels** | Altura y Peso | Optional; spinbutton for keyboard and screen readers |
| **Refer today** (after an urgent reading) | Ante una Emergencia | Books the referral with reason and priority filled in |
| **Patient record cards + BP trend** | Metrics / Hearth Activity | Latest readings with flags in words; BP chart with 140/90 lines |

## Next, by milestone

### Fri 30 Oct: connected patient and vitals (workplan checkpoint)
1. **Real API.** Swap the demo sign-in for the phone number + code flow (Figma *Completa tu Registro* + *Validar Registro*), and point sync at Bernard's endpoints.
2. **Registered! screen** (Figma *Felicidades*) after saving a patient, with "Record vitals now" and "Done".
3. **Sex as picture tiles** (Figma *Indica tu Género*) instead of a dropdown: one tap, works for low literacy.
4. **Weight and height wheels** (Figma *Altura y Peso*), with cm/kg toggles: fewer typos than typing.

### Sun 1 Nov: appointments, referral and summary
5. **Patient record as cards** (Figma *Metrics* / *Hearth Activity*): latest BP, temperature and weight cards, plus a BP trend chart over visits.
6. **Refer today screen** (Figma *Ante una Emergencia*): shown when a reading is urgent. Big "Book referral" plus "Call facility".
7. **Referral status** for nurses and doctors: requested → accepted → seen (mock flow with Bernard).
8. **District summary for officers** (Figma *Activity / Month*: Day / Week / Month tabs): counts of patients seen, urgent readings and referrals by week, anonymised, from Liza's endpoint.

### Stretch (if time, otherwise documented)
9. **Wound/skin photos** on the patient record (brief, week 1): camera, compressed on the phone, uploaded to the private S3 bucket with a presigned URL.
10. **Voice notes to text** on the vitals notes field (brief, week 1): the browser's speech API in English for now.
11. **Welcome + language screen** on first open (Figma *Welcome*), then the install prompt.
12. **Thumbprint consent** as a mock (brief: biometrics for patients who can't read).

## Open questions for the team
- **Germain:** is a phone number + SMS code enough for health workers, or do nurses and doctors need a password too?
- **Liza:** what shape is the summary (weeks × counts)? Which flags count as "urgent" for the district?
- **Bernard:** referral states, and who can accept one?
