import React from 'react';
import { Assistants } from './Assistants';
import { Commands } from './Commands';
import { Events } from './Events';
import { Intro } from './Intro';
import { KBoard } from './KBoard';
import { Launcher } from './Launcher';
import { Macros } from './Macros';
import { Outro } from './Outro';
import { Overview } from './Overview';
import { Progress } from './Progress';
import { QuickMenuScene } from './QuickMenu';
import { Remote } from './Remote';
import { Settings } from './Settings';
import { Voice } from './Voice';

/** The picture of each chapter of script/<lang>.json, by its id. */
export const SCENES: Record<string, React.FC> = {
  intro: Intro,
  overview: Overview,
  commands: Commands,
  quickmenu: QuickMenuScene,
  launcher: Launcher,
  events: Events,
  macros: Macros,
  kboard: KBoard,
  voice: Voice,
  remote: Remote,
  progress: Progress,
  settings: Settings,
  assistants: Assistants,
  outro: Outro,
};
