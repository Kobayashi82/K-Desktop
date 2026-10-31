import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../../../src/components/Chapter';
import { Mono } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { Showcase } from '../../../src/components/Showcase';
import { ease, glide, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { Code as CodeFile, Terminal } from './Parts';

/** The template from NuGet, a new project and its first build (site/en/sdk/getting-started.md). */
const Template: React.FC = () => {
  const B = useBeats();
  const x = B('template');
  return (
    <AbsoluteFill>
      <Terminal width={1300} height={420} style={{ left: 310, top: 240 }} lines={[
        { at: x.cue('install') - 6, cmd: 'dotnet new install KDesktop.Assistant.Templates' },
        { at: x.cue('new') - 8, cmd: 'dotnet new kdassistant -n MyAssistant --authorName "Me"' },
        { at: x.cue('new') + 30, out: 'The template "K-Desktop assistant" was created successfully.', color: C.teal },
        { at: x.cue('build') - 10, cmd: 'cd MyAssistant; dotnet build' },
        { at: x.cue('build') + 16, out: 'MyAssistant -> bin\\Debug\\…\\myassistant-1.0.0.kai', color: C.accentText },
      ]} />
    </AbsoluteFill>
  );
};

// The smallest assistant (site/en/sdk/getting-started.md), shortened.
const SMALLEST = [
  'public sealed class MyAssistant : IAssistant',
  '{',
  '    public void Attach(IAssistantHost host)',
  '    {',
  '        _surface = host.Rendering.CreateSurface();',
  '        _hit = host.Pointer.CreateHitArea();',
  '    }',
  '',
  '    public double Tick(TickInfo tick)',
  '    {',
  '        _surface.Present(area, dc => dc.DrawEllipse(…));',
  '        _hit.Place(new HitShape.Ellipse(area));',
  '        return 30;   // nothing moves: sleep',
  '    }',
  '}',
];

/** IAssistant: it draws itself, has a click area, and Tick says how long it may sleep. */
const Class: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const c = B('class');
  const k = glide(f, c.from - 8, 22);
  return (
    <AbsoluteFill style={{ opacity: k }}>
      <CodeFile file="MyAssistant.cs" width={1240} size={25} style={{ left: 340, top: 150 }} lines={SMALLEST} lit={[
        { from: c.cue('iassistant') - 4, to: c.cue('draws') - 4, lines: [0, 0] },
        { from: c.cue('draws') - 4, to: c.cue('hit') - 4, lines: [10, 10] },
        { from: c.cue('hit') - 4, to: c.cue('tick') - 4, lines: [11, 11] },
        { from: c.cue('tick') - 4, to: c.to + 20, lines: [8, 12] },
      ]} />
    </AbsoluteFill>
  );
};

// The gestures (site/en/sdk/gestures.md).
const GESTURES = ['PointAt', 'GoTo', 'PressButton', 'MoveWindow', 'ActivateWindow', 'WaitForWindow', 'Mark', 'Scan', 'Typing', 'Communicate', 'React', 'Announce'];

/** What the module asks for, never how; Communicate and React are enough. */
const Gestures: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const g = B('gestures');
  const lit: Record<string, number> = { PressButton: g.cue('press'), MoveWindow: g.cue('move'), PointAt: g.cue('point') };
  const min = ease(f, g.cue('min') - 4, g.cue('min') + 8, OUT);
  const clips: Array<[string, number]> = [['Lander/window.minimize', g.cue('press') - 4], ['Kumo/window.move', g.cue('move') - 4], ['Lander/gesture.pointAt', g.cue('point') - 4]];
  const current = [...clips].reverse().find(([, at]) => f >= at);
  const clip = current ? pop(f, clips[0][1], { damping: 15 }) * (1 - min) : 0;
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 120, top: 200, width: 900, display: 'flex', flexWrap: 'wrap', gap: 14 }}>
        {GESTURES.map((name, i) => {
          const k = pop(f, g.cue('gestures') - 6 + i * 2, { damping: 15 });
          const on = lit[name] !== undefined ? ease(f, lit[name] - 4, lit[name] + 6, OUT) * (1 - min) : 0;
          const base = name === 'Communicate' || name === 'React' ? min : 0;
          const gold = Math.max(on, base);
          return (
            <div key={name} style={{
              padding: '14px 22px', borderRadius: 14, background: gold > 0.5 ? 'linear-gradient(180deg, #FFD970, #F2B807)' : 'rgba(40,40,48,0.94)',
              border: `1.5px solid ${gold > 0.5 ? 'transparent' : C.line2}`, opacity: Math.min(1, k * 1.4) * (1 - 0.6 * min * (1 - base)), transform: `scale(${(0.8 + 0.2 * k) * (1 + 0.06 * gold)})`,
            }}><Mono size={26} color={gold > 0.5 ? '#1a1306' : C.text}>{name}</Mono></div>
          );
        })}
      </div>
      <div style={{ position: 'absolute', left: 120, top: 560, width: 900, fontSize: 34, fontWeight: 700, color: C.accentText, opacity: min }}>{t('Enough to work: the module does the rest itself')}</div>
      {current && (
        <div style={{ position: 'absolute', left: 1080, top: 230, opacity: Math.min(1, clip * 1.4), transform: `scale(${0.85 + 0.15 * clip})` }}>
          <Showcase key={current[0]} clip={current[0]} width={720} from={current[1]} pause={30} />
        </div>
      )}
      <Sfx at={g.cue('min') - 4} name="chime" volume={0.2} />
      {clips.map(([c, at]) => <Sfx key={c} at={at} name="blip" volume={0.12} />)}
    </AbsoluteFill>
  );
};

/** Chapter «With code». */
export const Code: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const at = (id: string) => B(id).from - 6;
  return (
    <AbsoluteFill>
      <Part from={0} to={at('class')} first><Template /></Part>
      <Part from={at('class')} to={at('gestures')}><Class /></Part>
      <Part from={at('gestures')} to={durationInFrames} last><Gestures /></Part>
    </AbsoluteFill>
  );
};
