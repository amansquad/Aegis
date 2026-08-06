"use client";

import { useEffect, useRef } from "react";

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Focus management for a fixed-overlay dialog or drawer: moves focus into the container on
 * mount, keeps Tab cycling inside it rather than leaking back into the page behind the backdrop,
 * treats Escape as the same action as the close button, and restores focus to whatever triggered
 * the dialog once it unmounts.
 *
 * `onClose` is read through a ref rather than an effect dependency so a new closure on every
 * render (the common shape here — `onClose={() => setCreating(false)}`) doesn't re-run the
 * mount/focus setup; only Escape and Tab wiring should be live across the dialog's lifetime.
 */
export function useDialogA11y<T extends HTMLElement>(onClose: () => void) {
  const containerRef = useRef<T | null>(null);
  const onCloseRef = useRef(onClose);

  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
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
  }, []);

  return containerRef;
}
