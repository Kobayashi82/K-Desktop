import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { ease, glide, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** Chapter «One more thing»: a file dropped on an assistant, and the window that asks what to do (its real texts). */
export const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const drag = ease(f, tip.cue('drag') - 4, tip.cue('drag') + 30, OUT);
  const dropped = f > tip.cue('drag') + 30;
  const win = glide(f, tip.cue('what') - 14, 20);
  const picture = f >= tip.cue('describe') - 6;
  const file = picture ? 'holiday.jpg' : 'report-q3.pdf';
  const buttons = picture
    ? [{ cue: 'describe', t: t('What is it?') }, { cue: '', t: t('Read its text') }, { cue: '', t: t('Open') }, { cue: '', t: t('Open its folder') }]
    : [{ cue: 'inside', t: t('What\'s inside?') }, { cue: 'summarize', t: t('Summarize') }, { cue: 'read', t: t('Read it to me') }, { cue: '', t: t('Open') }];
  const hot = buttons.findIndex((b, i) => b.cue && f >= tip.cue(b.cue) - 4 && (!buttons[i + 1]?.cue || f < tip.cue(buttons[i + 1].cue) - 4));
  const typed = t('Translate it into Spanish').slice(0, Math.max(0, Math.floor((f - tip.cue('tell') + 2) * 1.4)));
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      {/* The assistant as a target (no character is staged), and the file flying onto it. */}
      <div style={{ position: 'absolute', left: 300, top: 470, transform: `translate(-50%, -50%) scale(${1 + 0.12 * Math.sin(Math.min(1, Math.max(0, (f - tip.cue('drag') - 30) / 10)) * Math.PI)})` }}>
        <KdIcon icon="bot_24_filled" color="purple" size={180} glow={dropped ? 0.6 : 0.2} />
      </div>
      {!dropped && (
        <div style={{ position: 'absolute', left: 1500 - 1200 * drag, top: 300 + 170 * drag, transform: `translate(-50%, -50%) scale(${1 - 0.4 * drag}) rotate(${(1 - drag) * 10}deg)`, opacity: pop(f, tip.cue('drag') - 14) }}>
          <KdIcon icon="document_pdf_24_filled" color="red" size={130} />
        </div>
      )}
      <div style={{
        position: 'absolute', left: 560, top: 230, width: 1180, padding: '30px 34px', borderRadius: 20, boxSizing: 'border-box',
        background: 'rgba(36,36,42,0.98)', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)', opacity: win, transform: `translateX(${(1 - win) * 60}px)`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, fontSize: 32, fontWeight: 700, color: C.text }}>
          <KdIcon icon={picture ? 'image_24_filled' : 'document_pdf_24_filled'} color={picture ? 'sky' : 'red'} size={50} />
          {t('What should I do with «{0}»?').replace('{0}', file)}
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14, marginTop: 26 }}>
          {buttons.map((b, i) => (
            <div key={b.t} style={{
              height: 66, display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: 12, fontSize: 26, fontWeight: 600,
              color: i === hot ? '#1a1306' : C.text, background: i === hot ? 'linear-gradient(180deg, #FFD970, #F2B807)' : 'rgba(255,255,255,0.07)',
              transform: `scale(${i === hot ? 1.03 : 1})`,
            }}>{b.t}</div>
          ))}
        </div>
        <div style={{
          display: 'flex', alignItems: 'center', gap: 12, height: 62, marginTop: 18, padding: '0 20px', borderRadius: 12, fontSize: 24,
          border: `1.5px solid ${f >= tip.cue('tell') - 4 ? C.accentText : C.line2}`, background: 'rgba(0,0,0,0.25)', color: typed ? C.text : C.faint,
        }}>
          <Fluent name="mic_24_regular" size={26} color={C.dim} />
          {typed || t('Or tell Kobii what to do with it…')}
        </div>
      </div>
      <Sfx at={tip.cue('drag') + 30} name="pop-low" volume={0.25} />
      <Sfx at={tip.cue('what') - 14} name="whoosh-short" volume={0.2} />
      {['inside', 'summarize', 'read', 'describe'].map(c => <Sfx key={c} at={tip.cue(c) - 4} name="blip" volume={0.14} />)}
    </AbsoluteFill>
  );
};
