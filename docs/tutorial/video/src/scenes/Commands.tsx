import React from 'react';
import { AbsoluteFill } from 'remotion';
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
      <Part from={0} to={run.from - 6} first>
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
