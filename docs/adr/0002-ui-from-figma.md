# ADR 0002: UI design from the HealthCheck Figma kit

- **Status:** Accepted
- **Date:** 2026-10-26 (design chosen the same day)

## Context
For AgroConnect, the UI that worked came from Figma and real designers' kits, not from hand-made mockups. HealthConnect needs the same, adapted to clinical work: forms that are hard to get wrong, clear warnings, and large touch targets.

## Decision
- **Source:** the community kit *Redesign HealthCheck Medical Mobile App* ([team copy in Figma](https://www.figma.com/design/ZmlpQabab8CLksdZN9O7I5/)), chosen by the frontend lead. We use its style guide, components and screen patterns, and leave out its smartwatch, steps, sleep and stress content, which doesn't fit community health work.
- **Tokens** (`frontend/src/index.css`) come from its Color Guide and screen variables: Azul Principal `#0066FF`, screen `#F2F3F5`, title `#242731`, body `#1C1B1F`, card outline `#B6B6B6` with 12 px corners, field outline `#7C828F` with 4 px corners, navigation surface `#EEE8F4`, accent orange `#FA5805`.
- **Font: Poppins**, as in the kit. This reverses AgroConnect's system-font choice (ADR 0011 there): the design depends on it, and self-hosting only the Latin subset in 4 weights costs about 31 KB, downloaded once and cached by the service worker.
- **Patterns mapped to our screens:**

  | Figma | HealthConnect |
  |---|---|
  | Login | Sign-in (demo roles for now) |
  | Bottom Navigation (Inicio, Estadísticas, Agregar, Contactos, Mi Perfil) | Home, Patients, **Add** (orange when active), Visits, Profile |
  | Mis Contactos (search, outlined cards with a monogram, floating +) | Patient list |
  | Añadir Contacto forms with a progress stepper | Register a patient in 3 steps: consent, about the patient, where they live |
  | Home Screen cards | Home: the worker's card, "refer today" alert, number cards, sync |
  | Top Navbar "← Regresar" | "← Back" on every sub-page |

- **Accessibility adjustments** to the kit:
  - The kit's grey `#7C828F` is 3.5:1 on the screen background, too faint for small text. Small text uses `#5C616C` (5.6:1).
  - White text on the brand orange is only 3.3:1, so the orange carries icons only.
  - Urgent text uses a darker orange `#B34000` (5.7:1).
  - Status is never shown by colour alone.
  - Touch targets are at least 44 px.
- **Desktop:** the kit is phone-only. From 768 px the same places move to a sidebar on the navigation surface.

## Consequences
- Screens were checked as rendered (phone width, production build, offline) before review.
- Twi, Ewe, Dagbani and Hausa text must come from native speakers. Until then, English shows.
