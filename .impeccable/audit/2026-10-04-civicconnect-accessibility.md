# CivicConnect accessibility audit

Date: 2026-10-04. Scope: keyboard navigation, semantic ticket links, forms, focus, light/dark contrast, mobile navigation, and touch targets. No implementation changes made.

## Health

| Dimension | Score /4 | Evidence |
|---|---:|---|
| Accessibility | 2 | Semantic links now work, but form naming and mobile focus coverage remain incomplete |
| Performance | 3 | Small shared theme script and no obvious blocking animation; no formal performance benchmark |
| Responsive design | 3 | Ticket cards fit 320/390/768px and mobile controls measure at least 44px; mobile input typography still presents a zoom risk |
| Theming | 3 | Sampled dark colors pass; several light text pairs fail 4.5:1 |
| Implementation integrity | 2 | Coherent shared navigation/theme/table system; selected states and form associations are not consistently implemented |
| Total | 13/20 | Acceptable, significant accessibility work remains |

Implementation identity passes: the navy/teal system is coherent. Accessibility completeness does not pass. The bundled detector returned an empty findings array; manual checks found gaps outside its coverage.

Issues: P0 0, P1 3, P2 3, P3 0.

## P1 findings

1. Form controls lack associated accessible labels. Login.razor lines 17–18, NewTicket.razor line 4, Settings.razor line 3, and MainLayout.razor line 17. Browser DOM shows no associated labels or aria-label for email/password, ticket title/category/priority/description, and display name. Search has an empty wrapping label and relies on placeholder fallback. Add stable IDs and label for associations; give search an explicit accessible name. WCAG 1.3.1 and 4.1.2. Workflow: impeccable harden.

2. Open mobile navigation allows focus behind its overlay. MainLayout.razor lines 12–24 and app.css menu-open rules. Reproduced at 390×844: Tab from the theme button focused the underlying New Ticket button while the visible element at its center was a navigation link. The background is not inert. Opening-menu focus and Escape restoration work, but subsequent traversal reaches obscured content. Use a properly managed drawer, or a non-overlay disclosure; keep covered content out of keyboard traversal and restore focus on close. Focus-order and focus-not-obscured concern; this is not a full WCAG conformance determination. Workflow: impeccable harden.

3. Light-mode contrast failures. app.css shared muted/warning/primary-soft roles. Computed foreground/background results: warning/medium badge 3.85:1, active filter 4.18:1, table headings 4.48:1. These are small text, requiring 4.5:1 under WCAG 1.4.3. Dark equivalents passed at 6.69:1, 5.79:1, and 6.33:1. Adjust light text colors while retaining the same brand hues and layouts. Workflow: impeccable colorize.

## P2 findings

4. Selected ticket filter state is visual only. MyTickets.razor line 5. Active class exists, but aria-pressed and aria-selected are absent on all four filters. Expose aria-pressed on these buttons and name the filter group. Workflow: impeccable harden.

5. Sign-in error is not announced. Login.razor line 19. Entering an incorrect password inserts a plain paragraph with neither role=alert nor aria-live; focus remains on the current control. Provide an appropriately timed live error region and associate relevant fields with the message. Actual screen-reader announcements were not tested. Workflow: impeccable harden.

6. Mobile form text is below 16px. app.css field typography and narrow search rules. Ticket controls compute to 13.76px at 390px, and narrow search is 12px. This presents a native iOS input-zoom risk. Use at least 16px for editable controls on mobile while preserving desktop typography. Not a claimed WCAG failure and not reproduced on a physical iPhone. Workflow: impeccable adapt.

## Verified positives

- Ticket titles are real anchors with local href values, appear as links in the accessibility tree, receive Tab focus, and open details with Enter.
- Focused ticket links have a visible 3px outline.
- Table headings retain scope=col; mobile visual rearrangement retains explicit table/row/cell roles and visually hidden headers.
- Menu has aria-controls, aria-expanded, and a dynamic accessible name. Opening focuses Workspace; Tab enters its links; Escape restores the menu button; route changes close it.
- Requester, Staff, and Management menu destinations and mobile sign-out are available.
- Measured mobile menu/theme/filter targets are at least 44px. At 320px and 768px, page width equals viewport width. Desktop retains its normal table and hidden menu button.
- Theme toggle is named and keyboard-accessible. Shared dark text/status pairs pass sampled contrast checks: table headings 6.33, secondary ticket text 7.04, status colors 6.12–6.75, primary button 7.54.
- Status labels communicate meaning with text as well as color.

## Limits and next steps

Native Chrome accessibility trees, screenshots, read-only DOM measurements, keyboard traversal, source review, and CLI detector were used. This is not an axe scan, screen-reader certification, all-state contrast audit, real-device touch test, or performance benchmark. Desktop controls below 44px are not automatically WCAG AA failures: WCAG 2.2 AA uses a 24px target criterion with exceptions; 44px is the project's touch comfort target and the enhanced criterion.

Recommended order: impeccable harden for labels/focus/filter/error state; impeccable colorize for light contrast; impeccable adapt for mobile input typography; impeccable polish after those repairs. Preserve the current visual identity throughout.
