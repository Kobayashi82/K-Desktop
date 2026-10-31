import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent } from '../../../src/components/Icon';
import { Phone } from '../../../src/components/Phone';
import { Sfx } from '../../../src/components/Sfx';
import { Showcase } from '../../../src/components/Showcase';
import { ease, glide, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { Chat } from '../../11-telegram/scenes/Chat';
import { blips } from './Install';

/** «How you talk to it» in the Lander's settings (lander-settings.png): its word and its key. */
const Word: React.FC = () => {
  const B = useBeats();
  const w = B('word');
  const focus: Focus[] = [
    { from: w.cue('word') - 4, to: w.cue('key') - 4, rect: [580, 370, 942, 40], label: t('Its word'), at: 'bottom' },
    { from: w.cue('key') - 4, to: w.to + 20, rect: [580, 416, 942, 40], label: t('Hold its key'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="lander-settings" focus={focus}
        shots={[{ at: 0, x: 960, y: 540, s: 0.96, rx: 6 }, { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 }, { at: w.cue('word') - 16, dur: 26, x: 1050, y: 380, s: 1.55 }]} />
      {blips(focus)}
    </>
  );
};

// The right-click menu (App.AssistantMenu.cs): write, its extras, the panel, its settings, hide.
const MENU: Array<string | null> = [
  t('Write to Lander…'), null, t('Manual mode'), t('Offer a mission'), t('Start a race'), null, t('Assistant panel'), t('Lander settings'), null, t('Hide Lander'),
];
const MENU_ICONS: Record<number, string> = { 0: 'keyboard_24_regular', 6: 'bot_24_regular', 7: 'settings_24_regular', 9: 'eye_off_24_regular' };
const LIT = [0, 7, 9];

/** An assistant's right-click menu (App.AssistantMenu.cs): null is a separator; `lit` is the item under the pointer. */
export const AssistantMenu: React.FC<{ items: Array<string | null>; icons: Record<number, string>; lit?: number; open: number; style?: React.CSSProperties }> = ({ items, icons, lit, open, style }) => (
  <div style={{
    position: 'absolute', width: 400, padding: '8px 6px', borderRadius: 10, boxSizing: 'border-box', fontFamily: SANS,
    background: 'rgba(44,44,50,0.98)', border: '1px solid rgba(255,255,255,0.12)', boxShadow: '0 20px 50px rgba(0,0,0,0.55)',
    transform: `scale(${0.9 + 0.1 * open})`, transformOrigin: 'left bottom', opacity: Math.min(1, open * 1.5), ...style,
  }}>
    {items.map((item, i) => item === null
      ? <div key={i} style={{ height: 1, margin: '6px 10px', background: 'rgba(255,255,255,0.12)' }} />
      : (
        <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '10px 14px', borderRadius: 6, fontSize: 24, color: C.text, background: lit === i ? 'rgba(255,255,255,0.1)' : undefined }}>
          <div style={{ width: 24 }}>{icons[i] && <Fluent name={icons[i]} size={22} color={C.text} />}</div>
          {item}
        </div>
      ))}
  </div>
);

/** Pressing and holding on the Lander (its real «listening» clip), then its right-click menu. */
const Mouse: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const m = B('mouse');
  const hold = m.cue('hold') - 4;
  const menu = m.cue('menu') - 4;
  const clip = glide(f, m.from - 8, 22);
  // The Lander sits at 77 % × 81 % of the clip.
  const W = 1200, L = 160, T = 120;
  const lx = L + 0.77 * W, ly = T + 0.81 * (W * 9 / 16);
  const press = f >= hold && f < menu - 6;
  const ring = press ? ((f - hold) % 30) / 30 : 0;
  const open = pop(f, menu + 4, { damping: 16, stiffness: 180 });
  const words = [m.cue('write') - 2, m.cue('settings') - 2, m.cue('hide') - 2];
  const lit = words.findIndex((w, i) => f >= w && (i === words.length - 1 || f < words[i + 1]));
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: L, top: T, opacity: clip, transform: `translateY(${(1 - clip) * 40}px)` }}>
        <Showcase clip="Lander/state.listening" width={W} from={m.from - 8} />
      </div>
      {f >= hold - 10 && f < menu + 4 && (
        <>
          {press && <div style={{ position: 'absolute', left: lx - 20 - 50 * ring, top: ly - 20 - 50 * ring, width: 40 + 100 * ring, height: 40 + 100 * ring, borderRadius: '50%', border: `4px solid ${C.accentText}`, opacity: 1 - ring }} />}
          <div style={{ position: 'absolute', left: lx - 4, top: ly - 4, transform: `scale(${press ? 0.9 : 1})`, opacity: ease(f, hold - 10, hold) }}>
            <Fluent name="cursor_24_filled" size={52} color="#fff" style={{ filter: 'drop-shadow(0 2px 4px rgba(0,0,0,0.6))' }} />
          </div>
          <div style={{
            position: 'absolute', left: lx - 150, top: ly - 200, padding: '10px 22px', borderRadius: 14, fontSize: 28, fontWeight: 800, color: '#1a1306',
            background: 'linear-gradient(180deg, #FFD970, #F2B807)', opacity: ease(f, hold, hold + 10, OUT) * (1 - ease(f, menu - 8, menu)),
          }}>{t('Press and hold, and speak')}</div>
        </>
      )}
      {f >= menu && <AssistantMenu items={MENU} icons={MENU_ICONS} lit={LIT[lit]} open={open} style={{ left: lx + 20, top: ly - 470 }} />}
      <Sfx at={hold} name="blip-low" volume={0.15} />
      <Sfx at={menu} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Panel › Assistant (assistant-panel.png): the conversation and the activity. */
const Panel: React.FC = () => {
  const B = useBeats();
  const p = B('panel');
  const focus: Focus[] = [
    { from: p.cue('panel') - 4, to: p.cue('chat') - 4, rect: [12, 426, 216, 40] },
    { from: p.cue('chat') - 4, to: p.cue('activity') - 4, rect: [563, 217, 977, 310], label: t('Chat'), at: 'bottom' },
    { from: p.cue('activity') - 4, to: p.cue('phone') - 4, rect: [563, 543, 977, 56], label: t('Activity'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="assistant-panel" focus={focus}
        shots={[{ at: p.from - 10, x: 960, y: 540, s: 1.0 }, { at: p.cue('chat') - 16, dur: 26, x: 1000, y: 420, s: 1.3 }]} />
      {blips(focus)}
    </>
  );
};

const Bubble: React.FC<{ me?: boolean; at: number; children: React.ReactNode }> = ({ me, at, children }) => {
  const f = useCurrentFrame();
  const k = pop(f, at, { damping: 15, stiffness: 160 });
  if (f < at) return null;
  return (
    <div style={{
      alignSelf: me ? 'flex-end' : 'flex-start', maxWidth: '82%', padding: '12px 16px', fontSize: 20, lineHeight: 1.35, color: me ? '#1a1306' : C.text,
      borderRadius: me ? '16px 16px 4px 16px' : '16px 16px 16px 4px', background: me ? C.accentText : 'rgba(255,255,255,0.09)',
      opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 20}px)`,
    }}>{children}</div>
  );
};

/** From the phone (drawn: its Assistant tab) and from Telegram (the chat of chapter 11). */
const Elsewhere: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const p = B('panel');
  const phone = pop(f, p.cue('phone') - 8, { damping: 15 });
  const tg = pop(f, p.cue('telegram') - 8, { damping: 15 });
  const a = p.cue('phone') + 6;
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 560, top: 540, transform: `translate(-50%, -50%) scale(${0.7 + 0.3 * phone})`, opacity: Math.min(1, phone * 1.4) }}>
        <Phone width={400}>
          <div style={{ position: 'absolute', inset: 0, background: '#121216', padding: '70px 20px 0', boxSizing: 'border-box', display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div style={{ fontSize: 32, fontWeight: 800, color: C.text }}>{t('Assistant')}</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, fontSize: 20, color: C.dim, marginBottom: 10 }}>
              <Fluent name="bot_24_filled" size={24} color={C.accentText} /> {t('Lander')}
            </div>
            <Bubble me at={a}>{t('Open my music')}</Bubble>
            <Bubble at={a + 24}>{t('Done. Music is on its way.')}</Bubble>
            <div style={{ position: 'absolute', left: 20, right: 20, bottom: 36, height: 56, borderRadius: 28, background: 'rgba(255,255,255,0.07)', display: 'flex', alignItems: 'center', padding: '0 20px', fontSize: 19, color: C.faint }}>
              {t('Write to Lander…')}
              <Fluent name="mic_24_filled" size={26} color={C.accentText} style={{ marginLeft: 'auto' }} />
            </div>
          </div>
        </Phone>
      </div>
      <div style={{ position: 'absolute', left: 1380, top: 540, width: 640, height: 620, transform: `translate(-50%, -50%) scale(${0.7 + 0.3 * tg})`, opacity: Math.min(1, tg * 1.4) }}>
        <Chat width={640} height={620} msgs={[
          { at: p.cue('telegram') - 2, me: true, voice: 4 },
          { at: p.cue('telegram') + 22, text: t('Tomorrow: sunny, 21 °C. Good flying weather.') },
        ]} />
      </div>
      <Sfx at={p.cue('phone') - 8} name="whoosh-short" volume={0.2} />
      <Sfx at={p.cue('telegram') - 8} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Chapter «Talk to it». */
export const Talk: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const p = B('panel');
  return (
    <AbsoluteFill>
      <Part from={0} to={B('mouse').from - 6} first><Word /></Part>
      <Part from={B('mouse').from - 6} to={p.from - 6}><Mouse /></Part>
      <Part from={p.from - 6} to={p.cue('phone') - 10}><Panel /></Part>
      <Part from={p.cue('phone') - 10} to={durationInFrames} last><Elsewhere /></Part>
    </AbsoluteFill>
  );
};
