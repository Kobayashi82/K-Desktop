import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { blips } from './Builtin';

/** «Custom» (tools-files.png): one file per tool, and what kinds there are. */
const Custom: React.FC = () => {
  const B = useBeats();
  const j = B('json');
  const intro: Focus['rect'] = [578, 128, 940, 34];
  const focus: Focus[] = [
    { from: j.cue('custom') - 4, to: j.cue('file') - 4, rect: [261, 640, 266, 30] },
    { from: j.cue('file') - 4, to: j.cue('web') - 4, rect: [595, 232, 912, 268], label: t('One file per tool'), at: 'bottom' },
    { from: j.cue('web') - 4, to: j.cue('commands') - 4, rect: [595, 236, 912, 66], label: t('Website or API'), at: 'bottom' },
    { from: j.cue('commands') - 4, to: j.cue('steps') - 4, rect: intro },
    { from: j.cue('steps') - 4, to: j.cue('script') - 4, rect: [595, 329, 912, 66], label: t('Several steps'), at: 'bottom' },
    { from: j.cue('script') - 4, to: j.cue('load') - 4, rect: intro },
    { from: j.cue('load') - 4, to: j.to + 20, rect: intro, label: t('They load by themselves when you save the file'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="tools-files" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: j.cue('file') - 16, dur: 26, x: 1050, y: 330, s: 1.5 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** An MCP server (tools-mcp-server.png): its tools, when it runs, and its key in Secrets. */
const Mcp: React.FC = () => {
  const B = useBeats();
  const m = B('mcp');
  const focus: Focus[] = [
    { from: m.cue('mcp') - 4, to: m.cue('mcp') + 40, rect: [261, 672, 266, 182] },
    { from: m.cue('mcp') + 40, to: m.cue('start') - 4, rect: [580, 272, 942, 620] },
    { from: m.cue('start') - 4, to: m.cue('secrets') - 4, rect: [580, 154, 162, 20], label: t('Stopped (starts when used)'), at: 'bottom' },
    { from: m.cue('secrets') - 4, to: m.to + 30, rect: [930, 154, 143, 20], label: t('Secrets'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="tools-mcp-server" focus={focus}
        shots={[
          { at: m.from - 6, x: 960, y: 540, s: 1.0 },
          { at: m.cue('mcp') + 30, dur: 26, x: 1050, y: 520, s: 1.15 },
          { at: m.cue('start') - 16, dur: 26, x: 900, y: 220, s: 1.7 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** Chapter «Your own tools». */
export const Own: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const m = B('mcp');
  return (
    <AbsoluteFill>
      <Part from={0} to={m.from - 4} first><Custom /></Part>
      <Part from={m.from - 4} to={durationInFrames} last><Mcp /></Part>
    </AbsoluteFill>
  );
};
