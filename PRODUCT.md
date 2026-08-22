# Product

## Register

product

## Users

Windows users who want to capture material throughout the day without interrupting their work. They save screenshots, narrated screen walkthroughs, audio notes, text snippets, and web links, with screenshots and audio used most often.

## Product Purpose

CursorPocket is a quiet capture utility that stays close at hand, saves every capture locally into one organized folder, and makes recording an idea or preserving something on screen feel immediate. Success means a user can capture without hunting for the app, memorizing difficult shortcuts, or wondering where the result went.

## Brand Personality

Discreet, immediate, dependable. The product should feel like a native utility that disappears into the user’s workflow while remaining easy to find when needed.

## Anti-references

Avoid dashboard-like control centers, decorative productivity UI, floating widgets whose purpose is unexplained, dense settings screens, and workflows that require memorizing several modifier-heavy shortcuts. Do not make the user read instructions before their first successful capture.

## Design Principles

1. Capture first: screenshots and audio must be obvious and reachable in one action.
2. Teach through the interface: labels and state should explain the dot, shortcuts, save location, and recording status at the moment they matter.
3. Stay quiet by default: minimal persistent chrome, no decorative motion, and no interruption of the user’s current task.
4. Make recovery effortless: always show where a capture was saved and provide a direct path to the folder.
5. Prefer recognition over recall: visible actions and configurable shortcuts beat memorized key combinations.
6. Make recording unambiguous: screen, microphone, and webcam capture starts only after an explicit action and stays named on screen until it is saved or discarded.

## Product decisions on the annotation editor

Recorded here because the design system forbids adding sharing-adjacent surface without one, and because these should not be relitigated as design tweaks.

- **Exported backdrops and drop shadows: adopted.** A screenshot can be exported on a flat ground with a rendered shadow, which gives a tight crop room to breathe. Mesh gradients were declined: a gradient generator inside the app leaks into its own chrome, and a flat ground does the actual job.
- **Pinned captures: adopted.** A pin is the content itself, left on screen by explicit action, never restored after a restart. It is a receipt the user decided to keep, which is what distinguishes it from a floating widget whose purpose is unexplained.
- **OCR: adopted, local only.** Reading text out of a screenshot uses the engine built into Windows. No sidecar process, no model download, no network. The result becomes an ordinary text capture.
- **Still excluded:** any account, upload, share target, short link, or analytics. Nothing added here sends a capture anywhere.

## Accessibility & Inclusion

No additional user-specific accommodations were requested. Preserve native Windows keyboard navigation, readable contrast, visible focus, and non-color recording status as quality defaults.
