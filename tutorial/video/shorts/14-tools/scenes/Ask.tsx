import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Sfx } from '../../../src/components/Sfx';
import { glide, inOut } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { blips } from './Builtin';

/** «Ask before doing something», drawn with its real choices and hint. */
const Before: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const b = B('before');
  const card = glide(f, b.cue('before') - 12, 22);
  const opts = [{ cue: 'never', t: t('Never') }, { cue: 'risky', t: t('Only what\'s risky') }, { cue: 'first', t: t('Also the first time') }];
  const hot = opts.reduce((s, o, i) => (f >= b.cue(o.cue) - 4 ? i : s), -1);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{
        position: 'absolute', left: 960, top: 520, width: 1160, padding: '34px 40px', borderRadius: 28, boxSizing: 'border-box', transform: `translate(-50%, -50%) translateY(${(1 - card) * 60}px)`, opacity: card,
        background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      }}>
        <div style={{ fontSize: 34, fontWeight: 700, color: C.text }}>{t('Ask before doing something')}</div>
        <div style={{ fontSize: 22, color: C.dim, marginTop: 8, marginBottom: 28 }}>{t('«Only what\'s risky»: shutting down or restarting, scripts, simulated keys, macros and web requests.')}</div>
        <div style={{ display: 'flex', gap: 16 }}>
          {opts.map((o, i) => (
            <div key={o.cue} style={{
              flex: 1, textAlign: 'center', padding: '20px 10px', borderRadius: 16, fontSize: 28, fontWeight: 700,
              color: i === hot ? '#1a1306' : C.dim, background: i === hot ? 'linear-gradient(180deg, #FFD970, #F2B807)' : 'rgba(255,255,255,0.05)',
              border: `1.5px solid ${i === hot ? 'transparent' : C.line2}`, transform: `scale(${i === hot ? 1.04 : 1})`,
            }}>{o.t}</div>
          ))}
        </div>
      </div>
      {opts.map(o => <Sfx key={o.cue} at={b.cue(o.cue) - 4} name="blip" volume={0.14} />)}
    </AbsoluteFill>
  );
};

const CHOICES = [t('Without asking'), t('The first time'), t('Always')];

/** Each tool's own choice (tools-mcp-server.png), with its list open. */
const Each: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const e = B('each');
  const focus: Focus[] = [
    { from: e.cue('each') - 4, to: e.cue('safe') - 4, rect: [1256, 398, 132, 430] },
    { from: e.cue('safe') - 4, to: e.cue('once') - 4, rect: [1256, 480, 132, 28] },
    { from: e.cue('once') - 4, to: e.to + 30, rect: [1266, 400, 120, 28] },
  ];
  const list = inOut(f, e.cue('once') + 8, e.to + 60, 6);
  const hot = f >= e.cue('always') - 4 ? 2 : 1;
  return (
    <>
      <CaptureShot name="tools-mcp-server" focus={focus}
        shots={[{ at: e.from - 6, x: 1150, y: 560, s: 1.3 }, { at: e.from, dur: 24, x: 1200, y: 560, s: 1.6 }]}>
        <div style={{
          position: 'absolute', left: 1200, top: 436, width: 190, padding: 5, borderRadius: 8, background: '#2c2c30', border: '1px solid rgba(255,255,255,0.14)',
          boxShadow: '0 12px 30px rgba(0,0,0,0.55)', opacity: list, transform: `translateY(${(1 - list) * -8}px)`,
        }}>
          {CHOICES.map((c, i) => (
            <div key={c} style={{
              height: 30, display: 'flex', alignItems: 'center', padding: '0 10px', borderRadius: 5, fontFamily: SANS, fontSize: 14, color: C.text,
              background: i === hot ? 'rgba(255,255,255,0.1)' : 'transparent', borderLeft: `3px solid ${i === hot ? C.accentText : 'transparent'}`,
            }}>{c}</div>
          ))}
        </div>
      </CaptureShot>
      {blips(focus)}
      <Sfx at={e.cue('always') - 4} name="blip" volume={0.14} />
    </>
  );
};

/** Chapter «When it asks first». */
export const Ask: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const e = B('each');
  return (
    <AbsoluteFill>
      <Part from={0} to={e.from - 4} first><Before /></Part>
      <Part from={e.from - 4} to={durationInFrames} last><Each /></Part>
    </AbsoluteFill>
  );
};
