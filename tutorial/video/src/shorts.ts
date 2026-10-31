import React from 'react';
import { GETTING_STARTED } from '../shorts/01-getting-started/scenes';
import { COMMANDS } from '../shorts/02-commands/scenes';
import { QUICKMENU_LAUNCHER } from '../shorts/03-quickmenu-launcher/scenes';
import { MACROS_TEXTS } from '../shorts/04-macros-texts/scenes';
import { KBOARD } from '../shorts/05-kboard/scenes';
import { EVENTS } from '../shorts/06-events/scenes';
import { WINDOWS_CLIPBOARD_SCREEN } from '../shorts/07-windows-clipboard-screen/scenes';
import { VOICE } from '../shorts/08-voice/scenes';
import { CALENDAR_MAIL } from '../shorts/10-calendar-mail/scenes';
import { TELEGRAM } from '../shorts/11-telegram/scenes';
import { BRAIN } from '../shorts/13-brain/scenes';
import { TOOLS } from '../shorts/14-tools/scenes';

/** A short video (shorts/<id>): the picture of each chapter of its script, and the background colours of its chapters. */
export type Short = { scenes: Record<string, React.FC>; moods?: Record<string, readonly [string, string, string]> };

/** The short videos, by the id of their folder in shorts/ (tools/tts.py --short <id>, node render.mjs --short <id>). */
export const SHORTS: Record<string, Short> = {
  '01-getting-started': GETTING_STARTED,
  '02-commands': COMMANDS,
  '03-quickmenu-launcher': QUICKMENU_LAUNCHER,
  '04-macros-texts': MACROS_TEXTS,
  '05-kboard': KBOARD,
  '06-events': EVENTS,
  '07-windows-clipboard-screen': WINDOWS_CLIPBOARD_SCREEN,
  '08-voice': VOICE,
  '10-calendar-mail': CALENDAR_MAIL,
  '11-telegram': TELEGRAM,
  '13-brain': BRAIN,
  '14-tools': TOOLS,
};
