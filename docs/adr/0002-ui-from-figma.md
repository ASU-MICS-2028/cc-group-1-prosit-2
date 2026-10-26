# ADR 0002: UI design from a Figma file

- **Status:** Accepted
- **Date:** 2026-10-26

## Context
For AgroConnect, the UI that worked came from Figma and real designers' kits, not from hand-made mockups. HealthConnect needs the same, adapted to clinical work: forms that are hard to get wrong, clear warnings, and large touch targets.

## Decision
- The visual design is made in a **HealthConnect Figma file** built from a real health or medical community UI kit. The frontend is built from its frames and variables.
- Until the file exists, screens use **placeholder tokens** (AgroConnect's, with a teal primary) in `frontend/src/index.css`. The structure, flows and validation are real; only the look changes when the Figma variables arrive.
- Rules that hold whatever the look: status is never shown by colour alone (each state also has an icon and words); urgent vitals are red with "refer today"; touch targets are at least 44 px; the system font is used.

## Consequences
- Screens are functional now, so backend integration doesn't wait for the design.
- Restyling later changes tokens and markup only, not data or flows.
