import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { Mono } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { ease, glide, inOut, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

const LINK = 'https://shop.example.com/lamp?id=42';
const TRACKING = '&utm_source=mail&fbclid=IwAR3x';

/** A tick box like the editor's. */
const Check: React.FC<{ on: number; label: string }> = ({ on, label }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 16, fontSize: 22, color: C.text, marginTop: 14 }}>
    <div style={{
      width: 30, height: 30, borderRadius: 7, boxSizing: 'border-box', display: 'flex', alignItems: 'center', justifyContent: 'center',
      border: `2px solid ${on > 0.5 ? C.accent : C.line2}`, background: `rgba(224,168,21,${on})`, transform: `scale(${1 + 0.15 * Math.sin(on * Math.PI)})`,
    }}>
      {on > 0.5 && <Fluent name="checkmark_24_filled" size={22} color="#1a1306" />}
    </div>
    {label}
  </div>
);

/** Chapter «One more thing»: the Clipboard action's modes, then changing the selected text in place. */
export const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const card = glide(f, tip.cue('action') - 10, 22);
  const modes = [
    { cue: 'upper', name: t('UPPER CASE'), before: t('meeting moved to thursday'), after: t('MEETING MOVED TO THURSDAY') },
    { cue: 'plainText', name: t('Remove formatting'), before: t('Meeting moved to Thursday'), after: t('Meeting moved to Thursday'), rich: true },
    { cue: 'clean', name: t('Clean links (tracking)'), before: LINK + TRACKING, after: LINK, mono: true },
  ];
  const mode = modes.reduce((s, m, i) => (f >= tip.cue(m.cue) - 6 ? i : s), 0);
  const m = modes[mode];
  const swap = f < tip.cue(modes[0].cue) - 6 ? 1 : pop(f, tip.cue(m.cue) - 6, { damping: 15 });
  const done = ease(f, tip.cue(m.cue) + 6, tip.cue(m.cue) + 16, OUT);
  const first = ease(f, tip.cue('first') - 2, tip.cue('first') + 6);
  const after = ease(f, tip.cue('after') - 2, tip.cue('after') + 6);
  const chat = glide(f, tip.cue('first') - 14, 22);
  const sample = 1 - chat;
  const fix = ease(f, tip.cue('after') + 26, tip.cue('after') + 38, OUT);
  const selected = inOut(f, tip.cue('first') + 10, tip.cue('after') + 30, 6);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      {/* The action's editor */}
      <div style={{
        position: 'absolute', left: 130, top: 250, width: 760, padding: 34, borderRadius: 28, boxSizing: 'border-box',
        background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
        opacity: Math.min(1, card * 1.4), transform: `translateY(${(1 - card) * 60}px)`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 20, marginBottom: 28 }}>
          <KdIcon icon="clipboard_text_edit_24_filled" color="brown" size={64} />
          <span style={{ fontSize: 34, fontWeight: 700, color: C.text }}>{t('Clipboard')}</span>
        </div>
        <div style={{ fontSize: 20, color: C.dim, marginBottom: 8 }}>{t('Mode')}</div>
        <div style={{
          display: 'flex', alignItems: 'center', height: 58, padding: '0 20px', borderRadius: 10, fontSize: 24, color: C.text, overflow: 'hidden',
          background: 'rgba(255,255,255,0.06)', border: `1.5px solid rgba(242,193,74,${0.2 + 0.6 * inOut(f, tip.cue('upper') - 8, tip.cue('clean') + 30, 8)})`,
        }}>
          <span style={{ display: 'inline-block', transform: `translateY(${(1 - swap) * 30}px)`, opacity: Math.min(1, swap * 1.5) }}>{m.name}</span>
          <Fluent name="chevron_down_24_regular" size={22} color={C.dim} style={{ marginLeft: 'auto' }} />
        </div>
        <Check on={first} label={t('Copy the selection first (Ctrl+C)')} />
        <Check on={after} label={t('Paste afterwards (Ctrl+V)')} />
      </div>
      {/* What the mode does to a copied text */}
      <div style={{
        position: 'absolute', left: 990, top: 300, width: 800, opacity: Math.min(1, card * 1.4) * sample, transform: `translateX(${(1 - card) * 60 - chat * 60}px)`,
      }}>
        <div style={{ padding: '22px 26px', borderRadius: 18, background: 'rgba(255,255,255,0.05)', border: `1px solid ${C.line2}`, fontSize: 28, color: C.dim, minHeight: 50 }}>
          {m.mono ? <Mono size={21} color={C.dim}>{LINK}<span style={{ color: C.danger }}>{TRACKING}</span></Mono>
            : m.rich ? <span><b style={{ color: '#ff8cc6', fontFamily: 'Georgia, serif', fontSize: 32 }}>{t('Meeting moved')}</b> <i style={{ color: '#55d2ff', textDecoration: 'underline' }}>{t('to Thursday')}</i></span>
              : m.before}
        </div>
        <div style={{ display: 'flex', justifyContent: 'center', margin: '16px 0' }}><Fluent name="arrow_down_24_filled" size={40} color={C.accentText} style={{ opacity: done }} /></div>
        <div style={{
          padding: '22px 26px', borderRadius: 18, background: C.accentSoft, border: '1.5px solid rgba(242,193,74,0.5)', fontSize: 28, color: C.text, minHeight: 50,
          opacity: done, transform: `translateY(${(1 - done) * -20}px)`,
        }}>
          {m.mono ? <Mono size={21} color={C.text}>{m.after}</Mono> : m.after}
        </div>
      </div>
      {/* In place: a link selected in a chat changes where it is */}
      <WinFrame width={900} height={250} title={t('Chat')} bar={40} bg="#202228" icon={<KdIcon icon="chat_24_filled" color="blue" size={20} />}
        style={{ left: 940, top: 300, opacity: Math.min(1, chat * 1.4), transform: `translateX(${(1 - chat) * 80}px)` }}>
        <div style={{ padding: '26px 28px', display: 'flex', flexDirection: 'column', gap: 18 }}>
          <div style={{ alignSelf: 'flex-start', padding: '12px 18px', borderRadius: 14, background: 'rgba(255,255,255,0.08)', fontSize: 22, color: C.text }}>{t('Which lamp did you mean?')}</div>
          <div style={{ display: 'flex', alignItems: 'center', minHeight: 64, padding: '0 18px', borderRadius: 12, border: `1.5px solid ${C.line2}`, background: 'rgba(0,0,0,0.25)', fontSize: 22, color: C.text, whiteSpace: 'nowrap' }}>
            {t('This one:')}&nbsp;
            <span style={{ background: `rgba(0,120,215,${0.75 * selected})`, borderRadius: 3, padding: '2px 0' }}>
              <Mono size={17} color={C.text}>{LINK}<span style={{ display: 'inline-block', overflow: 'hidden', verticalAlign: 'bottom', whiteSpace: 'nowrap', maxWidth: `${(1 - fix) * 400}px`, color: C.text }}>{TRACKING}</span></Mono>
            </span>
            <span style={{ opacity: f % 30 < 15 ? 1 : 0 }}>▍</span>
          </div>
        </div>
      </WinFrame>
      {modes.map(x => <Sfx key={x.cue} at={tip.cue(x.cue) - 6} name="blip" volume={0.14} />)}
      <Sfx at={tip.cue('action') - 10} name="whoosh-short" volume={0.22} />
      <Sfx at={tip.cue('first')} name="click" volume={0.25} />
      <Sfx at={tip.cue('after')} name="click" volume={0.25} />
      <Sfx at={tip.cue('after') + 26} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};
