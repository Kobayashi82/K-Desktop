import React from 'react';
import { GETTING_STARTED } from '../shorts/01-getting-started/scenes';
import { COMMANDS } from '../shorts/02-commands/scenes';
import { QUICKMENU_LAUNCHER } from '../shorts/03-quickmenu-launcher/scenes';
import { MACROS_TEXTS } from '../shorts/04-macros-texts/scenes';
import { KBOARD } from '../shorts/05-kboard/scenes';
import { EVENTS } from '../shorts/06-events/scenes';
import { VOICE } from '../shorts/08-voice/scenes';

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
  '08-voice': VOICE,
};
