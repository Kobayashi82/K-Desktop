import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Part } from '../../../src/components/Chapter';
import { KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { Showcase } from '../../../src/components/Showcase';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { AssistantMenu } from './Talk';

/**
 * One assistant: its real clips on the left (each from its frame on), its name and what it brings on the right, as
 * chips that appear as they are named.
 */
const Spot: React.FC<{ b: BeatTimes; name: string; icon: string; text: string; clips: Array<[string, number]>; chips: Array<[string, number]> }> = ({ b, name, icon, text, clips, chips }) => {
  const f = useCurrentFrame();
  const k = glide(f, b.from - 8, 22);
  const current = [...clips].reverse().find(([, at]) => f >= at) ?? clips[0];
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 110, top: 190, opacity: k, transform: `translateX(${(1 - k) * -60}px)` }}>
        <Showcase key={current[0]} clip={current[0]} width={1040} from={current[1]} />
      </div>
      <div style={{ position: 'absolute', left: 1220, top: 210, width: 600, opacity: k, transform: `translateX(${(1 - k) * 60}px)` }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 22 }}>
          <Img src={staticFile(icon)} style={{ width: 96, height: 96 }} />
          <div style={{ fontSize: 64, fontWeight: 800, color: C.text }}>{name}</div>
        </div>
        <div style={{ fontSize: 28, color: C.dim, marginTop: 16, lineHeight: 1.35 }}>{text}</div>
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 14, marginTop: 34 }}>
          {chips.map(([label, at]) => {
            const c = pop(f, at, { damping: 15 });
            return (
              <div key={label} style={{
                padding: '12px 24px', borderRadius: 30, fontSize: 28, fontWeight: 700, color: C.text, background: 'rgba(40,40,48,0.94)',
                border: `1.5px solid ${C.line2}`, opacity: Math.min(1, c * 1.4), transform: `translateX(${(1 - c) * 50}px)`,
              }}>{label}</div>
            );
          })}
        </div>
      </div>
      {chips.map(([label, at]) => <Sfx key={label} at={at} name="pop-high" volume={0.14} />)}
    </AbsoluteFill>
  );
};

const Lander: React.FC = () => {
  const b = useBeats()('lander');
  return (
    <Spot b={b} name={t('Lander')} icon="brand/lander/icon.png" text={t('A little lunar module that flies around your windows and lands on them. Game included.')}
      clips={[['Lander/lander.loop', b.from - 8], ['Lander/lander.confetti', b.cue('game') - 4]]}
      chips={[[t('Manual mode · Ctrl + Alt + L'), b.cue('manual') - 4], [t('Missions'), b.cue('missions') - 4], [t('Races'), b.cue('races') - 4]]} />
  );
};

const Kobii: React.FC = () => {
  const b = useBeats()('kobii');
  return (
    <Spot b={b} name={t('Kobii')} icon="brand/kobii/icon.png" text={t('A squishy little creature that wanders along your taskbar and over your windows.')}
      clips={[['Kobii/kobii.eat', b.from - 8], ['Kobii/kobii.ball', b.cue('ball') - 4], ['Kobii/kobii.butterfly', b.cue('play') - 4]]}
      chips={[[t('Give it something to eat'), b.cue('feed') - 4], [t('Give it the ball'), b.cue('ball') - 4], [t('It plays on its own'), b.cue('play') - 4]]} />
  );
};

const Kumo: React.FC = () => {
  const b = useBeats()('kumo');
  return (
    <Spot b={b} name={t('Kumo')} icon="brand/kumo/icon.png" text={t('An amber cloud that floats over your windows, never stops changing shape and talks to you in its own cloud.')}
      clips={[['Kumo/kumo.wiggle', b.from - 8], ['Kumo/kumo.rainbow', b.cue('rainbow') - 4], ['Kumo/kumo.drizzle', b.cue('drizzle') - 4], ['Kumo/kumo.dance', b.cue('dance') - 4]]}
      chips={[[t('Make a rainbow'), b.cue('rainbow') - 4], [t('Drizzle'), b.cue('drizzle') - 4], [t('Dance'), b.cue('dance') - 4]]} />
  );
};

// Kobii's right-click menu, with its extras (assistant.json).
const KOBII_MENU: Array<string | null> = [
  t('Write to Kobii…'), null, t('Give it something to eat'), t('Give it something to drink'), t('Give it the ball'), t('Give it the teddy'),
  t('Put out its bed'), t('High five'), t('Shake hands'), t('Brush it'), null, t('Assistant panel'), t('Kobii settings'), null, t('Hide Kobii'),
];
const KOBII_ICONS: Record<number, string> = { 0: 'keyboard_24_regular', 11: 'bot_24_regular', 12: 'settings_24_regular', 14: 'eye_off_24_regular' };

/** Their extras: in the right-click menu, and as the «Assistant: action» action. */
const Extras: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const e = B('extras');
  const open = pop(f, e.cue('menu') - 6, { damping: 16, stiffness: 180 });
  const action = pop(f, e.cue('action') - 6, { damping: 15 });
  const lit = 2 + (Math.floor(Math.max(0, f - e.cue('menu')) / 8) % 8);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 150, top: 160 }}>
        <Showcase clip="Kobii/kobii.wiggle" width={640} from={e.from - 8} />
      </div>
      <AssistantMenu items={KOBII_MENU} icons={KOBII_ICONS} lit={lit} open={open} style={{ left: 840, top: 110 }} />
      <div style={{
        position: 'absolute', left: 1300, top: 520, display: 'flex', alignItems: 'center', gap: 22, padding: '20px 28px', borderRadius: 20,
        background: 'rgba(30,30,36,0.96)', border: `1.5px solid ${C.line2}`, boxShadow: '0 20px 50px rgba(0,0,0,0.5)', opacity: Math.min(1, action * 1.4),
        transform: `scale(${0.8 + 0.2 * action})`,
      }}>
        <KdIcon icon="sparkle_24_filled" color="pink" size={70} />
        <div>
          <div style={{ fontSize: 30, fontWeight: 700, color: C.text }}>{t('Assistant: action')}</div>
          <div style={{ fontSize: 22, color: C.dim, marginTop: 4 }}>{t('Kobii · Give it the ball')}</div>
        </div>
      </div>
      <Sfx at={e.cue('menu') - 6} name="pop" volume={0.2} />
      <Sfx at={e.cue('action') - 6} name="pop-high" volume={0.18} />
    </AbsoluteFill>
  );
};

/** Chapter «Each one, its own». */
export const Own: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const at = (id: string) => B(id).from - 6;
  return (
    <AbsoluteFill>
      <Part from={0} to={at('kobii')} first><Lander /></Part>
      <Part from={at('kobii')} to={at('kumo')}><Kobii /></Part>
      <Part from={at('kumo')} to={at('extras')}><Kumo /></Part>
      <Part from={at('extras')} to={durationInFrames} last><Extras /></Part>
    </AbsoluteFill>
  );
};
