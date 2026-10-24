import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { CaptureShot, useCapture } from '../components/Capture';
import { Part } from '../components/Chapter';
import { Fluent } from '../components/Icon';
import { Sfx } from '../components/Sfx';
import { ease, inOut, OUT, pop } from '../lib/anim';
import { BeatTimes, useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';

const HistoryPart: React.FC<{ b: BeatTimes }> = ({ b }) => (
  <CaptureShot name="history"
    shots={[
      { at: 0, x: 1000, y: 560, s: 1.0, rx: 6 },
      { at: b.from, dur: 60, x: 1060, y: 520, s: 1.12, rx: 0 },
      { at: b.cue('where') - 10, dur: 40, x: 700, y: 520, s: 1.35 },
      { at: b.cue('long') - 10, dur: 40, x: 1500, y: 540, s: 1.35 },
      { at: b.cue('wrong') - 10, dur: 40, x: 1000, y: 700, s: 1.3 },
    ]}
    focus={[
      { from: b.cue('five') - 6, to: b.cue('where') - 4, rect: [261, 118, 630, 22], label: 'The last 500 runs' },
      { from: b.cue('where') - 4, to: b.cue('long') - 4, rect: [338, 222, 330, 830], label: 'Where it came from', at: 'right' },
      { from: b.cue('long') - 4, to: b.cue('wrong') - 4, rect: [1828, 205, 64, 846], label: 'How long it took', at: 'left' },
      { from: b.cue('wrong') - 4, to: b.to + 30, rect: [262, 262, 1630, 46] },
      { from: b.cue('wrong') - 4, to: b.to + 30, rect: [262, 716, 1630, 42], label: 'What went wrong' },
    ]}
  />
);

/** A card in the style of the achievements page, for the ones out of the screenshot. */
const AchCard: React.FC<{ title: string; text: string; secret?: boolean; show: number; x: number; y: number }> = ({ title, text, secret, show, x, y }) => (
  <div style={{
    position: 'absolute', left: x, top: y, width: 560, display: 'flex', alignItems: 'center', gap: 24, padding: '26px 30px', borderRadius: 20,
    background: secret ? 'rgba(40,40,46,0.97)' : 'linear-gradient(180deg, rgba(70,58,20,0.97), rgba(44,38,22,0.97))', border: `2px solid ${secret ? C.line2 : 'rgba(242,193,74,0.7)'}`,
    boxShadow: '0 30px 70px rgba(0,0,0,0.6)', opacity: show, transform: `translateY(${(1 - show) * 60}px) scale(${0.9 + 0.1 * show})`,
  }}>
    <div style={{ width: 96, height: 96, borderRadius: 48, background: secret ? 'rgba(255,255,255,0.08)' : 'linear-gradient(180deg,#FFD970,#E0A815)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
      <Fluent name={secret ? 'lock_closed_24_filled' : 'weather_moon_24_filled'} size={50} color={secret ? C.faint : '#2a1d02'} />
    </div>
    <div>
      <div style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text }}>{title}</div>
      <div style={{ fontFamily: SANS, fontSize: 21, color: C.dim, marginTop: 4 }}>{text}</div>
    </div>
  </div>
);

const AchievementsPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const toast = useCapture('achievement-toast');
  const toastAt = b.cue('toast');
  const toastShow = inOut(f, toastAt - 6, b.cue('owl') - 4, 10);
  return (
    <AbsoluteFill>
      <CaptureShot name="achievements"
        shots={[
          { at: 0, x: 1000, y: 560, s: 1.05 },
          { at: b.cue('hundred') - 10, dur: 30, x: 1070, y: 330, s: 1.25 },
          { at: b.cue('pianist') - 10, dur: 30, x: 1180, y: 520, s: 1.6 },
          { at: b.cue('secret') - 10, dur: 40, x: 1000, y: 560, s: 1.0 },
        ]}
        focus={[
          { from: b.cue('hundred') - 6, to: toastAt - 4, rect: [263, 147, 1630, 106], label: '166 achievements' },
          { from: b.cue('pianist') - 6, to: b.cue('secret') - 4, rect: [1077, 473, 260, 103], label: 'Pianist', at: 'top' },
        ]}
      />
      {/* The notification, big */}
      <div style={{ position: 'absolute', left: 960, top: 540, transform: `translate(-50%, -50%) translateX(${(1 - ease(f, toastAt - 6, toastAt + 10, OUT)) * 600}px) scale(2.6)`, opacity: toastShow }}>
        <Img src={staticFile(toast.file)} style={{ width: toast.width, height: toast.height, borderRadius: 12, boxShadow: '0 20px 50px rgba(0,0,0,0.7), 0 0 60px rgba(242,193,74,0.35)' }} />
      </div>
      <AchCard title="Night owl" text="Run a command between 3 and 5 in the morning" show={inOut(f, b.cue('owl') - 6, b.cue('pianist') - 6, 10)} x={680} y={420} />
      <AchCard title="Secret achievement" text="Keep using K-Desktop to discover it" secret show={pop(f, b.cue('secret') - 4)} x={680} y={420} />
      <Sfx at={toastAt - 4} name="achievement" volume={0.6} />
      <Sfx at={b.cue('owl') - 6} name="pop" volume={0.25} />
      <Sfx at={b.cue('secret') - 4} name="pop-low" volume={0.25} />
    </AbsoluteFill>
  );
};

const StatsPart: React.FC<{ b: BeatTimes }> = ({ b }) => (
  <CaptureShot name="stats"
    shots={[
      { at: 0, x: 1000, y: 560, s: 1.0, rx: 4 },
      { at: b.cue('run') - 10, dur: 30, x: 1080, y: 300, s: 1.25, rx: 0 },
      { at: b.cue('voice') - 10, dur: 30, x: 1300, y: 900, s: 1.5 },
      { at: b.cue('sources') - 10, dur: 30, x: 1500, y: 450, s: 1.45 },
    ]}
    focus={[
      { from: b.cue('run') - 6, to: b.cue('menus') - 2, rect: [263, 147, 812, 93], label: 'Commands and actions run' },
      { from: b.cue('menus') - 4, to: b.cue('voice') - 4, rect: [1086, 147, 398, 93], label: 'QuickMenus opened' },
      { from: b.cue('voice') - 4, to: b.cue('sources') - 4, rect: [1077, 993, 790, 48], label: 'Voice orders · understood by meaning', at: 'top' },
      { from: b.cue('sources') - 4, to: b.to + 40, rect: [1247, 256, 646, 360], label: 'Where they come from', at: 'bottom' },
    ]}
  />
);

export const Progress: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const ach = B('achievements');
  const stats = B('stats');
  return (
    <AbsoluteFill>
      <Part from={0} to={ach.from - 6} first><HistoryPart b={B('history')} /></Part>
      <Part from={ach.from - 6} to={stats.from - 6}><AchievementsPart b={ach} /></Part>
      <Part from={stats.from - 6} to={durationInFrames} last><StatsPart b={stats} /></Part>
    </AbsoluteFill>
  );
};
