# Drawer full-front selection handle fix

## Change

The generated proxy previously used 125% of the projected half-width and only 22% of the drawer height: effectively a narrow handle strip.

It now derives full projected front width and height from the drawer-owned renderer bounds, then applies a configurable 4% inset on every outer edge. The resulting collider covers 92% of each front-panel dimension while retaining a 0.018-unit normal depth.

## Safety

The proxy remains on the outer front plane and moves as a child of the drawer. The shared semantic ray resolver is unchanged: direct internal-object hits still win, a direct proxy hit selects the parent drawer, and the large open-drawer body is still only a fallback.

## Diagnostics

`DRAWER_SELECTION_HANDLE_CREATED` now records `front_width`, `front_height`, `collider_size`, `local_position`, `front_normal`, and `margin` for every table and cabinet drawer.

## Tests

The existing EditMode proxy test now verifies 92% front coverage, thin normal depth, parent/canonical identity, and movement with the drawer transform.

## Device check

For every table and cabinet drawer: open it, point at the whole front panel, point directly at any internal object, then point into an empty cavity. Confirm drawer, object, and fallback selection respectively.
