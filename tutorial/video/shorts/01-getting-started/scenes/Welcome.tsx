import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../../../src/components/Camera';
import { FocusLayer, useCapture } from '../../../src/components/Capture';
import { CommandsPage } from '../../../src/components/CommandsPage';
import { Cursor } from '../../../src/components/Cursor';
import { Fluent } from '../../../src/components/Icon';
import { KdWindow, NAV_CURRENT } from '../../../src/components/KdWindow';
import { Sfx } from '../../../src/components/Sfx';
import { Callout } from '../../../src/components/Ui';
import { ease, glide, inOut, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

// The welcome window (real screenshots, 680 × 509) seen at this zoom, centred on the stage.
const W = 680;
const H = 509;
const ZOOM = 1.7;
const stage = (x: number, y: number) => ({ x: 960 + (x - W / 2) * ZOOM, y: 540 + (y - H / 2) * ZOOM });
const NEXT = stage(613, 461);
const START = stage(606, 471);

/** One capture of the welcome, shown while `k` is above zero. */
const Page: React.FC<{ name: string; k: number }> = ({ name, k }) => {
  const cap = useCapture(name);
  if (k <= 0) return null;
  return <Img src={staticFile(cap.file)} style={{ position: 'absolute', left: 0, top: 0, width: W, height: H, opacity: k }} />;
};

/** Chapter «The welcome»: its steps, one after another, with what each one sets. */
export const Welcome: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const first = B('first');
  const look = B('look');
  const what = B('what');
  const startup = B('startup');
  const keys = B('keys');
  const tray = B('tray');
  const optional = B('optional');

  // When each step comes in (the click on «Get started» / «Next» just before).
  const steps = [
    { name: 'welcome-start', at: 0 },
    { name: 'welcome-features', at: what.cue('what') - 6 },
    { name: 'welcome-basics', at: startup.from - 8 },
    { name: 'welcome-voice', at: optional.cue('voice') - 8 },
    { name: 'welcome-assistants', at: optional.cue('assistants') + 6 },
    { name: 'welcome-done', at: optional.cue('done') - 8 },
  ];
  const shown = steps.map((s, i) => {
    const next = steps[i + 1]?.at ?? durationInFrames + 10;
    return i === 0 ? 1 - ease(f, next, next + 6) : Math.min(ease(f, s.at, s.at + 6), 1 - ease(f, next, next + 6));
  });
  const clickAt = steps.slice(1).map(s => s.at - 3);

  const dialog = pop(f, first.cue('welcome') - 10, { damping: 15, stiffness: 120 });
  const backdrop = glide(f, 2, 26);
  const focus = [
    { from: look.cue('theme') - 4, to: look.cue('language') - 4, rect: [459, 249, 193, 32] as [number, number, number, number] },
    { from: look.cue('language') - 4, to: look.to + 6, rect: [412, 193, 240, 37] as [number, number, number, number] },
    { from: startup.cue('startup') + 4, to: keys.from, rect: [20, 126, 640, 48] as [number, number, number, number] },
    { from: keys.cue('launcher') - 4, to: keys.cue('show') - 2, rect: [20, 180, 640, 52] as [number, number, number, number] },
    { from: keys.cue('show') - 2, to: keys.to + 8, rect: [20, 232, 640, 46] as [number, number, number, number] },
    { from: tray.from, to: steps[3].at - 2, rect: [28, 288, 624, 58] as [number, number, number, number] },
  ];
  const cursor = [
    { at: 0, x: 1500, y: 760 },
    { at: steps[1].at - 22, x: START.x, y: START.y, dur: 14 },
    { at: steps[2].at - 20, x: NEXT.x, y: NEXT.y, dur: 12 },
  ];
  const trayCard = inOut(f, tray.cue('tray') - 8, steps[3].at + 10, 10);
  return (
    <AbsoluteFill>
      {/* K-Desktop opens behind the welcome */}
      <AbsoluteFill style={{ opacity: backdrop, filter: `blur(${4 * dialog}px) brightness(${1 - 0.45 * dialog})`, transform: `scale(${0.96 + 0.04 * backdrop})` }}>
        <div style={{ position: 'absolute', left: 180, top: 70 }}>
          <KdWindow page="commands" nav={NAV_CURRENT} assistant={false}>
            <CommandsPage list={[]} />
          </KdWindow>
        </div>
      </AbsoluteFill>

      <AbsoluteFill style={{ opacity: Math.min(1, dialog * 1.4), transform: `perspective(2200px) rotateX(${(1 - dialog) * 10}deg) translateY(${(1 - dialog) * 60}px)` }}>
        <Camera shots={[{ at: 0, x: W / 2, y: H / 2, s: ZOOM }]} width={W} height={H} float={0.4}>
          <div style={{ position: 'absolute', left: 0, top: 0, width: W, height: H, borderRadius: 8, overflow: 'hidden', background: '#202024', boxShadow: '0 30px 90px rgba(0,0,0,0.65), 0 0 0 1px rgba(255,255,255,0.12)' }}>
            {steps.map((s, i) => <Page key={s.name} name={s.name} k={shown[i]} />)}
            <FocusLayer focus={focus} width={W} height={H} />
          </div>
        </Camera>
      </AbsoluteFill>

      <Cursor path={cursor} clicks={clickAt.slice(0, 2).map(at => ({ at }))} show={1 - ease(f, steps[2].at + 10, steps[2].at + 20)} />

      {/* What a few settings mean */}
      <Callout show={inOut(f, startup.cue('admin') - 6, startup.to + 16, 8)} x={960} y={70} align="center" gold size={28}>
        {t('Administrator rights, no prompt')}
      </Callout>
      <div style={{
        position: 'absolute', right: 40, top: 640, display: 'flex', alignItems: 'center', gap: 18, padding: '14px 22px', borderRadius: 16,
        background: 'rgba(28,30,38,0.95)', border: `1px solid ${C.line2}`, boxShadow: '0 20px 50px rgba(0,0,0,0.5)', opacity: trayCard, transform: `translateY(${(1 - trayCard) * 30}px)`,
      }}>
        <Fluent name="chevron_up_24_regular" size={20} color={C.text} />
        <div style={{ padding: 6, borderRadius: 8, background: `rgba(242,193,74,${0.25 * inOut(f, tray.cue('tray'), tray.to + 20, 6)})` }}>
          <Img src={staticFile('brand/icon-64.png')} style={{ width: 30, height: 30, display: 'block' }} />
        </div>
        <Fluent name="wifi_1_24_regular" size={20} color={C.text} />
        <Fluent name="speaker_2_24_regular" size={20} color={C.text} />
        <div style={{ fontFamily: SANS, fontSize: 16, color: C.text, lineHeight: 1.25, textAlign: 'right' }}>9:41<br />30/10/2026</div>
      </div>

      <Sfx at={first.cue('welcome') - 10} name="whoosh-short" volume={0.3} />
      {clickAt.map(at => <Sfx key={at} at={at} name="click" volume={0.25} />)}
      {focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />)}
      <Sfx at={optional.cue('done') - 4} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};
