import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../../components/Chapter';
import { Fluent, KdIcon } from '../../components/Icon';
import { Mono } from '../../components/Keys';
import { WinFrame } from '../../components/KdWindow';
import { Osd } from '../../components/Osd';
import { Sfx } from '../../components/Sfx';
import { Toggle } from '../../components/Ui';
import { ease, glide, inOut, keys, pop, SMOOTH } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { t } from '../../lib/i18n';

const Bubble: React.FC<{ text: string; show: number; x: number; y: number; small?: boolean }> = ({ text, show, x, y, small }) => (
  <div style={{ position: 'absolute', left: x, top: y, transform: `translate(-50%, -50%) scale(${0.7 + 0.3 * show})`, opacity: show, padding: small ? '12px 22px' : '18px 32px', borderRadius: 30, background: 'white', fontFamily: SANS, fontSize: small ? 30 : 42, fontWeight: 800, color: '#141418', whiteSpace: 'nowrap', boxShadow: '0 20px 50px rgba(0,0,0,0.5)' }}>
    “{text}”
  </div>
);

/** One sentence, two orders. */
export const Chain: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const split = ease(f, b.cue('and'), b.cue('and') + 20, SMOOTH);
  const done = [b.cue('and') + 26, b.cue('and') + 40];
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 330, transform: 'translate(-50%, -50%)', display: 'flex', gap: 24 + split * 120, alignItems: 'center', opacity: pop(f, b.cue('open') - 8) }}>
        <div style={{ padding: '18px 32px', borderRadius: 30, background: 'white', fontFamily: SANS, fontSize: 44, fontWeight: 800, color: '#141418', whiteSpace: 'nowrap' }}>{t('open YouTube')}</div>
        <div style={{ fontFamily: SANS, fontSize: 44, fontWeight: 800, color: C.accentText, opacity: 1 - split * 0.6 }}>{t('and')}</div>
        <div style={{ padding: '18px 32px', borderRadius: 30, background: 'white', fontFamily: SANS, fontSize: 44, fontWeight: 800, color: '#141418', whiteSpace: 'nowrap' }}>{t('empty the recycle bin')}</div>
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 560, display: 'flex', justifyContent: 'center', gap: 60 }}>
        {[{ name: t('YouTube'), icon: 'video_clip_24_filled', color: 'red' }, { name: t('Empty Recycle Bin'), icon: 'broom_24_filled', color: 'orange' }].map((c, i) => {
          const k = pop(f, done[i] - 10);
          const ok = f > done[i];
          return (
            <div key={c.name} style={{ display: 'flex', alignItems: 'center', gap: 20, padding: '26px 34px', borderRadius: 26, background: 'rgba(34,34,40,0.96)', border: `2px solid ${ok ? 'rgba(108,203,95,0.7)' : C.line2}`, opacity: k, transform: `translateY(${(1 - k) * 60}px)` }}>
              <KdIcon icon={c.icon} color={c.color} size={70} />
              <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text }}>{c.name}</span>
              <Fluent name="checkmark_circle_24_filled" size={44} color={C.success} style={{ opacity: ok ? 1 : 0 }} />
            </div>
          );
        })}
      </div>
      <Sfx at={b.cue('and') + 4} name="whoosh-short" volume={0.2} />
      {done.map(d => <Sfx key={d} at={d} name="blip" volume={0.2} />)}
    </AbsoluteFill>
  );
};

/** Music and volume orders, no commands needed. */
export const Media: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const orders = [
    { cue: 'pause', text: t('Pause') }, { cue: 'next', text: t('Next song') }, { cue: 'quieter', text: t('A little quieter') }, { cue: 'forty', text: t('Volume to 40 percent') }, { cue: 'play', text: t('Play music') },
  ];
  const current = orders.reduce((c, o, i) => (f >= b.cue(o.cue) - 6 ? i : c), -1);
  const paused = current >= 0 && current < 4;
  const track = current >= 1 ? 'Midnight Drive' : 'Golden Hour';
  const volume = keys(f, [[b.cue('quieter'), 60], [b.cue('quieter') + 12, 50], [b.cue('forty'), 50], [b.cue('forty') + 12, 40]]);
  const progress = ((f - b.from) * (paused ? 0 : 1)) / 600;
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 260, top: 300, width: 760, padding: 34, borderRadius: 30, background: 'rgba(30,30,36,0.97)', border: `1px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)', transform: `translateY(${(1 - glide(f, b.from - 10, 24)) * 60}px)` }}>
        <div style={{ display: 'flex', gap: 30 }}>
          <div style={{ width: 220, height: 220, borderRadius: 20, background: current >= 1 ? 'linear-gradient(135deg,#3EDCC1,#1E62DA)' : 'linear-gradient(135deg,#FF8CC6,#7B3FE4)' }} />
          <div style={{ flex: 1, paddingTop: 20 }}>
            <div style={{ fontFamily: SANS, fontSize: 40, fontWeight: 800, color: C.text }}>{track}</div>
            <div style={{ fontFamily: SANS, fontSize: 24, color: C.dim, marginTop: 6 }}>{t('Synthwave Radio')}</div>
            <div style={{ display: 'flex', gap: 30, marginTop: 34, alignItems: 'center' }}>
              <Fluent name="previous_24_filled" size={44} color={C.text} />
              <div style={{ width: 84, height: 84, borderRadius: 42, background: C.accentText, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <Fluent name={paused ? 'play_24_filled' : 'pause_24_filled'} size={44} color="#1a1306" />
              </div>
              <Fluent name="next_24_filled" size={44} color={C.text} />
            </div>
          </div>
        </div>
        <div style={{ marginTop: 30, height: 8, borderRadius: 4, background: 'rgba(255,255,255,0.1)' }}><div style={{ width: `${(0.2 + progress) * 100 % 100}%`, height: '100%', borderRadius: 4, background: C.text }} /></div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginTop: 30 }}>
          <Fluent name="speaker_2_24_filled" size={34} color={C.teal} />
          <div style={{ flex: 1, height: 10, borderRadius: 5, background: 'rgba(255,255,255,0.1)' }}><div style={{ width: `${volume}%`, height: '100%', borderRadius: 5, background: C.teal }} /></div>
          <Mono size={28}>{`${Math.round(volume)} %`}</Mono>
        </div>
      </div>
      {orders.map((o, i) => (
        <Bubble key={o.cue} text={o.text} small x={1440} y={260 + i * 130} show={pop(f, b.cue(o.cue) - 8) * (i === current ? 1 : 0.45)} />
      ))}
      {orders.map(o => <Sfx key={o.cue} at={b.cue(o.cue) + 6} name="blip" volume={0.15} />)}
    </AbsoluteFill>
  );
};

/** Dictation into any window. */
export const Dictate: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const first = 'Hi Ana, the report is ready.';
  const second = 'I will send it tomorrow morning.';
  const t1 = first.slice(0, Math.max(0, Math.floor((f - b.cue('dictation')) / 1.4)));
  const nl = f > b.cue('newline') + 4;
  const t2 = nl ? second.slice(0, Math.max(0, Math.floor((f - b.cue('newline') - 4) / 1.0))) : '';
  const scratched = f > b.cue('scratch') + 6;
  const done = inOut(f, b.cue('stopd') + 6, b.to + 30, 6);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 260, top: 200, transform: `translateY(${(1 - glide(f, b.from - 10, 24)) * 60}px)` }}>
        <WinFrame width={1400} height={600} title={t('Email - New message')} style={{ position: 'relative' }} bg="#202228">
          <div style={{ padding: '40px 50px', fontFamily: SANS, fontSize: 40, color: C.text, lineHeight: 1.6 }}>
            <div>{t1}{!nl && <span style={{ display: 'inline-block', width: 3, height: 44, marginLeft: 4, background: C.accentText, verticalAlign: 'middle' }} />}</div>
            {nl && <div style={{ opacity: scratched ? 0.25 : 1, textDecoration: scratched ? 'line-through' : 'none', height: scratched ? 64 * (1 - ease(f, b.cue('scratch') + 10, b.cue('scratch') + 20)) : 'auto', overflow: 'hidden' }}>{t2}</div>}
          </div>
        </WinFrame>
      </div>
      <div style={{ position: 'absolute', left: 1300, top: 250, display: 'flex', flexDirection: 'column', gap: 20 }}>
        {[['newline', 'new line'], ['scratch', 'scratch that'], ['stopd', 'stop dictating']].map(([c, t]) => (
          <div key={c} style={{ padding: '12px 22px', borderRadius: 24, background: 'white', fontFamily: SANS, fontSize: 30, fontWeight: 800, color: '#141418', opacity: pop(f, b.cue(c) - 6), transform: `scale(${pop(f, b.cue(c) - 6)})` }}>“{t}”</div>
        ))}
      </div>
      <div style={{ position: 'absolute', left: 960, top: 900, transform: 'translateX(-50%)' }}>
        <Osd icon="mic_sparkle_24_filled" color="teal" text={t('Dictation: 6 words')} show={done} />
      </div>
      <Sfx at={b.cue('scratch') + 6} name="whoosh-down" volume={0.2} />
      <Sfx at={b.cue('stopd') + 6} name="blip" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Downloaded once, memory, ducking, privacy. */
export const Privacy: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const models = [
    { name: t('Parakeet'), what: t('speech to text'), icon: 'text_t_24_filled', color: 'indigo' },
    { name: t('Silero'), what: t('voice activity'), icon: 'pulse_24_filled', color: 'orange' },
    { name: 'openWakeWord', what: t('wake word features'), icon: 'data_histogram_24_filled', color: 'sky' },
    { name: t('EmbeddingGemma'), what: 'meaning', icon: 'brain_circuit_24_filled', color: 'teal' },
  ];
  const ram = keys(f, [[b.cue('ram'), 0], [b.cue('ram') + 30, 700], [b.cue('free') + 10, 700], [b.cue('free') + 40, 0]]);
  const duck = keys(f, [[b.cue('duck'), 80], [b.cue('duck') + 20, 25], [b.cue('never') - 10, 25], [b.cue('never'), 80]]);
  const shield = pop(f, b.cue('never') - 4, { damping: 12 });
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 150, top: 200, width: 780, padding: 30, borderRadius: 28, background: 'rgba(30,30,36,0.97)', border: `1px solid ${C.line2}` }}>
        <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text, marginBottom: 20 }}>{t('Voice models')}</div>
        {models.map((m, i) => {
          const k = ease(f, b.cue('once') + i * 6, b.cue('once') + i * 6 + 24);
          return (
            <div key={m.name} style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '14px 4px' }}>
              <KdIcon icon={m.icon} color={m.color} size={50} />
              <div style={{ flex: 1 }}>
                <div style={{ fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.text }}>{m.name}</div>
                <div style={{ fontFamily: SANS, fontSize: 19, color: C.dim }}>{m.what}</div>
              </div>
              <div style={{ width: 150, height: 8, borderRadius: 4, background: 'rgba(255,255,255,0.1)' }}><div style={{ width: `${k * 100}%`, height: '100%', borderRadius: 4, background: k >= 1 ? C.success : C.accentText }} /></div>
              <Fluent name="checkmark_circle_24_filled" size={30} color={C.success} style={{ opacity: k >= 1 ? 1 : 0 }} />
            </div>
          );
        })}
      </div>
      <div style={{ position: 'absolute', left: 1010, top: 200, width: 760, display: 'flex', flexDirection: 'column', gap: 28 }}>
        <div style={{ padding: 30, borderRadius: 28, background: 'rgba(30,30,36,0.97)', border: `1px solid ${C.line2}`, opacity: ease(f, b.cue('ram') - 10, b.cue('ram')) }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.text, marginBottom: 16 }}>
            <span>{t('Memory')}</span><Mono size={26} color={ram > 10 ? C.accentText : C.success}>{ram > 10 ? `${Math.round(ram)} MB` : t('freed')}</Mono>
          </div>
          <div style={{ height: 18, borderRadius: 9, background: 'rgba(255,255,255,0.08)' }}><div style={{ width: `${(ram / 700) * 100}%`, height: '100%', borderRadius: 9, background: C.accentText }} /></div>
          <div style={{ marginTop: 14, fontFamily: SANS, fontSize: 20, color: C.dim }}>{t('Free the memory: after 10 min unused')}</div>
        </div>
        <div style={{ padding: 30, borderRadius: 28, background: 'rgba(30,30,36,0.97)', border: `1px solid ${C.line2}`, opacity: ease(f, b.cue('duck') - 10, b.cue('duck')) }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 16 }}>
            <span style={{ flex: 1, fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.text }}>{t('Turn the volume down while you speak')}</span>
            <Toggle on={1} scale={1.3} />
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            <Fluent name="music_note_2_24_filled" size={30} color={C.dim} />
            <div style={{ flex: 1, height: 10, borderRadius: 5, background: 'rgba(255,255,255,0.08)' }}><div style={{ width: `${duck}%`, height: '100%', borderRadius: 5, background: C.teal }} /></div>
          </div>
        </div>
      </div>
      <div style={{ position: 'absolute', left: 960, top: 860, transform: `translate(-50%, -50%) scale(${shield})`, opacity: shield, display: 'flex', alignItems: 'center', gap: 22, padding: '22px 40px', borderRadius: 40, background: 'rgba(108,203,95,0.12)', border: `2px solid ${C.success}` }}>
        <Fluent name="shield_checkmark_24_filled" size={56} color={C.success} />
        <span style={{ fontFamily: SANS, fontSize: 36, fontWeight: 800, color: C.text }}>{t('Neither audio nor text leaves your PC')}</span>
      </div>
      <Sfx at={b.cue('once')} name="pop-low" volume={0.2} />
      <Sfx at={b.cue('never') - 4} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};

export const UsesPart: React.FC<{ chain: BeatTimes; media: BeatTimes; dictate: BeatTimes; privacy: BeatTimes; end: number }> = ({ chain, media, dictate, privacy, end }) => (
  <AbsoluteFill>
    <Part from={chain.from - 6} to={media.from - 6} first><Chain b={chain} /></Part>
    <Part from={media.from - 6} to={dictate.from - 6}><Media b={media} /></Part>
    <Part from={dictate.from - 6} to={privacy.from - 6}><Dictate b={dictate} /></Part>
    <Part from={privacy.from - 6} to={end} last><Privacy b={privacy} /></Part>
  </AbsoluteFill>
);
