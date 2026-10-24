import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot } from '../components/Capture';
import { Part } from '../components/Chapter';
import { useBeats, useChapter } from '../lib/timeline';
import { BuildPart, RunPart } from './commands/Build';
import { CatalogPart } from './commands/Catalog';
import { AdminPart, IconsPart, IfPart, OptionsPart } from './commands/Extras';

/** Chapter 2: build a command, run it, every action by family, «If…», the options, administrator, icons. */
export const Commands: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const run = B('run');
  const catalog = B('catalog');
  const ifBeat = B('if');
  const options = B('options');
  const admin = B('admin');
  const icons = B('icons');
  const families = {
    apps: B('cat-apps'), windows: B('cat-windows'), input: B('cat-input'), sound: B('cat-sound'),
    screen: B('cat-screen'), network: B('cat-network'), system: B('cat-system'), kdesktop: B('cat-flow'),
  };
  return (
    <AbsoluteFill>
      {/* The real page first; at «New command» the animated one takes over */}
      <Part from={0} to={B('page').cue('new') - 2} first>
        <CaptureShot name="commands"
          shots={[{ at: 0, x: 960, y: 540, s: 1.0, rx: 6 }, { at: 10, dur: 60, x: 960, y: 540, s: 1.04, rx: 0 }, { at: B('page').cue('new') - 40, dur: 34, x: 520, y: 330, s: 1.7 }]}
          focus={[{ from: B('page').cue('new') - 30, to: B('page').cue('new') + 10, rect: [255, 216, 252, 40] }]}
          cursor={[{ at: 0, x: 1200, y: 700 }, { at: B('page').cue('new') - 36, x: 390, y: 238, dur: 26 }]} clicks={[{ at: B('page').cue('new') - 6 }]} />
      </Part>
      <Part from={B('page').cue('new') - 2} to={run.from - 6}>
        <BuildPart page={B('page')} hotkey={B('hotkey')} hotkey2={B('hotkey2')} actions={B('actions')} to={run.from - 6} />
      </Part>
      <Part from={run.from - 6} to={catalog.from - 6}><RunPart run={run} /></Part>
      <Part from={catalog.from - 6} to={ifBeat.from - 6}><CatalogPart catalog={catalog} beats={families} to={ifBeat.from - 6} /></Part>
      <Part from={ifBeat.from - 6} to={options.from - 6}><IfPart b={ifBeat} /></Part>
      <Part from={options.from - 6} to={admin.from - 6}><OptionsPart b={options} /></Part>
      <Part from={admin.from - 6} to={icons.from - 6}><AdminPart b={admin} /></Part>
      <Part from={icons.from - 6} to={durationInFrames} last><IconsPart b={icons} /></Part>
    </AbsoluteFill>
  );
};
