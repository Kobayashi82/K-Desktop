import React from 'react';
import { Commands } from './Commands';
import { Events } from './Events';
import { Intro } from './Intro';
import { KBoard } from './KBoard';
import { Launcher } from './Launcher';
import { Macros } from './Macros';
import { Overview } from './Overview';
import { QuickMenuScene } from './QuickMenu';

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
};
