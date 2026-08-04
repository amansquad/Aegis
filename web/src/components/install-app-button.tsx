"use client";

import { useEffect, useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui";

/** The event Chromium fires when it decides this page is installable, per the manifest. */
interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

/**
 * "Download the web app" — a real install action, not a link to a store listing that does not
 * exist. Chromium-based browsers decide independently whether a page qualifies (manifest, icons,
 * HTTPS); this component only ever renders once the browser has already made that decision by
 * firing `beforeinstallprompt`, rather than showing a button that would silently do nothing on
 * Safari or an already-installed session.
 */
export function InstallAppButton({ className }: { className?: string }) {
  const [prompt, setPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  // Lazy initializer rather than an effect: this reads a standing fact about the current page
  // load, not something to synchronise after mount, and the component already renders nothing
  // until `prompt` is set regardless of this value, so there is no hydration mismatch to cause.
  const [installed, setInstalled] = useState(
    () => typeof window !== "undefined" && window.matchMedia("(display-mode: standalone)").matches,
  );

  useEffect(() => {
    const onBeforeInstallPrompt = (event: Event) => {
      // Chromium fires this automatically the moment it decides the page qualifies; suppressing
      // the default mini-infobar keeps the decision in this button rather than a second surface.
      event.preventDefault();
      setPrompt(event as BeforeInstallPromptEvent);
    };

    const onInstalled = () => {
      setInstalled(true);
      setPrompt(null);
    };

    window.addEventListener("beforeinstallprompt", onBeforeInstallPrompt);
    window.addEventListener("appinstalled", onInstalled);

    return () => {
      window.removeEventListener("beforeinstallprompt", onBeforeInstallPrompt);
      window.removeEventListener("appinstalled", onInstalled);
    };
  }, []);

  if (installed || !prompt) return null;

  async function handleClick() {
    if (!prompt) return;

    await prompt.prompt();
    await prompt.userChoice;
    // Each BeforeInstallPromptEvent can only be prompted once, accepted or not.
    setPrompt(null);
  }

  return (
    <Button variant="secondary" onClick={handleClick} className={className}>
      <Download size={14} aria-hidden />
      Download the app
    </Button>
  );
}
