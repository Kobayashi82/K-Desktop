import React from 'react';
import { After } from './After';
import { Brain } from './Brain';
import { Choose } from './Choose';
import { Commands } from './Commands';
import { Events } from './Events';
import { Intro } from './Intro';
import { Kinds } from './Kinds';
import { Letter } from './Letter';
import { Listen } from './Listen';
import { Loop } from './Loop';
import { MapScene } from './Map';
import { Meaning } from './Meaning';
import { Outro } from './Outro';
import { Processes } from './Processes';
import { Rules } from './Rules';
import { Run } from './Run';
import { Speak } from './Speak';
import { Turn } from './Turn';
import { Wake } from './Wake';

/** The picture of each chapter of script/<lang>.json, by its id. */
export const SCENES: Record<string, React.FC> = {
  intro: Intro,
  map: MapScene,
  processes: Processes,
  wake: Wake,
  listen: Listen,
  turn: Turn,
  rules: Rules,
  commands: Commands,
  meaning: Meaning,
  letter: Letter,
  run: Run,
  brain: Brain,
  choose: Choose,
  kinds: Kinds,
  loop: Loop,
  events: Events,
  speak: Speak,
  after: After,
  outro: Outro,
};
