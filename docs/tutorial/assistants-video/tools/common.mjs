import { existsSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

/** The newest headless shell Playwright has downloaded (Linux, Windows or macOS), if any. */
const playwrightShell = () => {
  const dirs = [process.env.PLAYWRIGHT_BROWSERS_PATH, '/opt/pw-browsers', join(homedir(), '.cache', 'ms-playwright'),
    process.env.LOCALAPPDATA && join(process.env.LOCALAPPDATA, 'ms-playwright'), join(homedir(), 'Library', 'Caches', 'ms-playwright')];
  const builds = ['chrome-headless-shell-linux64/chrome-headless-shell', 'chrome-headless-shell-win64/chrome-headless-shell.exe',
    'chrome-headless-shell-mac-arm64/chrome-headless-shell', 'chrome-headless-shell-mac-x64/chrome-headless-shell'];
  for (const dir of dirs.filter(d => d && existsSync(d))) {
    const shells = readdirSync(dir).filter(d => d.startsWith('chromium_headless_shell-'))
      .sort((a, b) => Number(b.split('-')[1]) - Number(a.split('-')[1]));
    for (const shell of shells)
      for (const build of builds)
        if (existsSync(join(dir, shell, build))) return join(dir, shell, build);
  }
  return null;
};

/** Chromium for rendering: REMOTION_BROWSER, Playwright's headless shell when it is there, or Remotion's own download. */
export const browserExecutable = process.env.REMOTION_BROWSER ?? playwrightShell();

export const webpackOverride = config => config;
