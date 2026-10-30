import React from 'react';
import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { useBeats } from '../../../src/lib/timeline';
import { IfPart } from '../../../src/scenes/commands/Extras';
import { MOODS } from '../../../src/theme';
import { Build } from './Build';
import { Catalog } from './Catalog';
import { Hook } from './Hook';
import { Launch } from './Launch';

/** The tip: the long video's «If…» scene. */
const Tip: React.FC = () => React.createElement(IfPart, { b: useBeats()('if') });

/** Tutorial 02, «Commands, actions and hotkeys»: build one, every action, the ways to launch it, and «If…». */
export const COMMANDS: Short = {
  scenes: { hook: Hook, build: Build, catalog: Catalog, launch: Launch, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.intro,
    build: MOODS.commands,
    catalog: MOODS.launcher,
    launch: MOODS.kboard,
    tip: MOODS.events,
    end: MOODS.outro,
  },
};
