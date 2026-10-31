import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

export const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

/** Settings › Tools (tools.png), then «Built-in» by subject (tools-builtin.png). */
const Groups: React.FC = () => {
  const B = useBeats();
  const g = B('groups');
  return (
    <>
      <CaptureShot name="tools"
        focus={[
          { from: g.cue('settings') - 4, to: g.cue('count') - 4, rect: [261, 560, 266, 46] },
          { from: g.cue('count') - 4, to: g.to, rect: [580, 159, 467, 77], label: t('83 built-in'), at: 'bottom' },
        ]}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: g.cue('count') - 16, dur: 26, x: 1000, y: 300, s: 1.5 },
        ]} />
      <Sfx at={g.cue('settings') - 4} name="blip" volume={0.12} />
      <Sfx at={g.cue('count') - 4} name="blip" volume={0.12} />
    </>
  );
};

const ROWS: Array<[string, Focus['rect']]> = [
  ['calendar', [595, 168, 912, 70]], ['files', [595, 370, 912, 80]], ['internet', [595, 480, 912, 72]], ['mail', [595, 573, 912, 72]],
  ['music', [595, 666, 912, 72]], ['notes', [595, 759, 912, 72]], ['pc', [595, 852, 912, 90]], ['screen', [595, 961, 912, 70]],
];

const Subjects: React.FC = () => {
  const B = useBeats();
  const g = B('groups');
  const c = B('choose');
  const focus: Focus[] = ROWS.map(([cue, rect], i) => ({ from: g.cue(cue) - 4, to: ROWS[i + 1] ? g.cue(ROWS[i + 1][0]) - 4 : c.from, rect }));
  return (
    <>
      <CaptureShot name="tools-builtin" focus={focus}
        shots={[
          { at: g.cue('calendar') - 14, x: 1050, y: 480, s: 1.2 },
          { at: g.cue('calendar') - 10, dur: 26, x: 1050, y: 480, s: 1.3 },
          { at: g.cue('pc') - 20, dur: 26, x: 1050, y: 700, s: 1.3 },
        ]} />
      {blips(focus)}
    </>
  );
};

const TOOLS = [
  t('Add an event'), t('Find files'), t('Read a web page'), t('Send a mail'), t('Control playback'), t('What\'s playing'),
  t('Save a note'), t('Open a program'), t('PC status'), t('Look at the screen'), t('Weather'), t('Change the volume'),
  t('Timer'), t('Look it up online'),
];
const PICKED = [4, 10];
const OFF = [2, 13];

/** Only the tools that fit the request go to the AI; switched-off ones never do. */
const Choose: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const c = B('choose');
  const ask = glide(f, c.from - 4, 20);
  const pick = glide(f, c.cue('picks') - 4, 16);
  const off = glide(f, c.cue('off') - 4, 16);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 230, display: 'flex', justifyContent: 'center', opacity: ask, transform: `translateY(${(1 - ask) * -30}px)` }}>
        <div style={{ padding: '18px 34px', borderRadius: 34, background: C.accentText, color: '#1a1306', fontSize: 36, fontWeight: 700 }}>“{t('Pause the music and tell me the weather')}”</div>
      </div>
      <div style={{ position: 'absolute', left: 260, right: 260, top: 400, display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: 16 }}>
        {TOOLS.map((name, i) => {
          const k = pop(f, c.from + i * 2, { damping: 15 });
          const picked = PICKED.includes(i);
          const isOff = OFF.includes(i) ? off : 0;
          const lit = picked ? pick : 0;
          const dim = picked ? 0 : pick;
          return (
            <div key={name} style={{
              display: 'flex', alignItems: 'center', gap: 10, padding: '14px 24px', borderRadius: 16, fontSize: 26, fontWeight: 600,
              color: lit > 0.5 ? '#1a1306' : C.text, background: lit > 0.5 ? 'linear-gradient(180deg, #FFD970, #F2B807)' : 'rgba(40,40,48,0.94)',
              border: `1.5px solid ${lit > 0.5 ? 'transparent' : C.line2}`, opacity: Math.min(1, k * 1.4) * (1 - 0.55 * dim) * (1 - 0.5 * isOff),
              transform: `scale(${(0.8 + 0.2 * k) * (1 + 0.08 * lit)})`, textDecoration: isOff > 0.5 ? 'line-through' : undefined,
              boxShadow: lit > 0.5 ? '0 0 40px rgba(242,193,74,0.45)' : undefined,
            }}>
              <Fluent name={isOff > 0.5 ? 'toggle_left_24_regular' : 'wrench_24_regular'} size={24} color={lit > 0.5 ? '#1a1306' : C.dim} />
              {name}
            </div>
          );
        })}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 700, textAlign: 'center', fontSize: 34, fontWeight: 700, color: C.accentText, opacity: pick }}>{t('Only these two go to the AI')}</div>
      <Sfx at={c.cue('picks') - 4} name="chime" volume={0.2} />
      <Sfx at={c.cue('off') - 4} name="click" volume={0.22} />
    </AbsoluteFill>
  );
};

/** «Test» on a tool (tools-file.png). */
const Test: React.FC = () => {
  const B = useBeats();
  const c = B('choose');
  return (
    <>
      <CaptureShot name="tools-file" focus={[{ from: c.cue('test') - 4, to: c.to + 30, rect: [1395, 209, 66, 30], label: t('Test'), at: 'bottom' }]}
        shots={[{ at: c.cue('test') - 14, x: 1100, y: 260, s: 1.4 }, { at: c.cue('test') - 10, dur: 24, x: 1150, y: 260, s: 1.7 }]} />
      <Sfx at={c.cue('test') - 4} name="blip" volume={0.12} />
    </>
  );
};

/** Chapter «What it can do». */
export const Builtin: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const g = B('groups');
  const c = B('choose');
  return (
    <AbsoluteFill>
      <Part from={0} to={g.cue('calendar') - 12} first><Groups /></Part>
      <Part from={g.cue('calendar') - 12} to={c.from - 6}><Subjects /></Part>
      <Part from={c.from - 6} to={c.cue('test') - 12}><Choose /></Part>
      <Part from={c.cue('test') - 12} to={durationInFrames} last><Test /></Part>
    </AbsoluteFill>
  );
};
