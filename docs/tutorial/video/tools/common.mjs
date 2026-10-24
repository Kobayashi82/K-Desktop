import { existsSync } from 'node:fs';

/** Chromium for rendering: REMOTION_BROWSER, Playwright's headless shell when it is there, or Remotion's own download. */
export const browserExecutable = process.env.REMOTION_BROWSER
  ?? ['/opt/pw-browsers/chromium_headless_shell-1243/chrome-headless-shell-linux64/chrome-headless-shell'].find(p => existsSync(p))
  ?? null;

export const webpackOverride = config => config;
