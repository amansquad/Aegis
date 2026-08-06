"use client";

import { useEffect, useRef } from "react";

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Focus management for a fixed-overlay dialog, drawer, or toggled panel: moves focus into the
 * container once active, keeps Tab cycling inside it rather than leaking back into the page
 * behind the backdrop, treats Escape as the same action as the close button, and restores focus
 * to whatever triggered it once it goes inactive.
 *
 * `active` defaults to `true` for the common case — a dialog component that only mounts while
 * shown, where "mounted" and "active" are the same moment. Pass the open flag explicitly for a
 * panel that stays mounted throughout, such as a nav drawer toggled by transform rather than by
 * conditional render, so focus is only stolen when it actually opens rather than on first paint.
 *
 * `onClose` is read through a ref rather than an effect dependency so a new closure on every
 * render (the common shape here — `onClose={() => setCreating(false)}`) doesn't re-run the
 * focus/Tab-wiring setup on every render, only when `active` itself flips.
 */
export function useDialogA11y<T extends HTMLElement>(onClose: () => void, active = true) {
  const containerRef = useRef<T | null>(null);
  const onCloseRef = useRef(onClose);

  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    if (!active) return;

    const container = containerRef.current;
    if (!container) return;

    const previouslyFocused = document.activeElement as HTMLElement | null;
    const initialFocusable = container.querySelectorAll<HTMLElement>(FOCUSABLE);
    (initialFocusable[0] ?? container).focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        onCloseRef.current();
        return;
      }

      if (event.key !== "Tab" || !container) return;

      const nodes = container.querySelectorAll<HTMLElement>(FOCUSABLE);
      if (nodes.length === 0) return;

      const first = nodes[0];
      const last = nodes[nodes.length - 1];

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      previouslyFocused?.focus();
    };
  }, [active]);

  return containerRef;
}
