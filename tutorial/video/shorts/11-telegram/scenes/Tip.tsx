import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { Toggle } from '../../../src/components/Ui';
import { ease, glide } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { Chat } from './Chat';

/** Chapter «One more thing»: «Telegram notices › Important emails», and one arriving. */
export const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const card = glide(f, tip.cue('notices') - 14, 22);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{
        position: 'absolute', left: 130, top: 330, width: 860, padding: '30px 34px', borderRadius: 26, boxSizing: 'border-box',
        background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
        opacity: card, transform: `translateY(${(1 - card) * 60}px)`,
      }}>
        <div style={{ fontSize: 30, fontWeight: 700, color: C.text }}>{t('Telegram notices')}</div>
        <div style={{ fontSize: 20, color: C.dim, marginTop: 6, marginBottom: 24 }}>{t('What K-Desktop sends you on its own.')}</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 18 }}>
          <KdIcon icon="mail_24_filled" color="blue" size={50} />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 25, fontWeight: 600, color: C.text }}>{t('Important emails')}</div>
            <div style={{ fontSize: 18, color: C.dim, marginTop: 4 }}>{t('The ones Settings › Mail marks as important.')}</div>
          </div>
          <Toggle on={ease(f, tip.cue('notices') + 10, tip.cue('notices') + 20)} scale={1.4} />
        </div>
      </div>
      <Chat width={720} height={700} style={{ left: 1080, top: 190, opacity: glide(f, tip.cue('notices') - 4, 22) }}
        msgs={[{ at: tip.cue('reach') - 6, text: t('Important email from Ana\nInvoice for October') }]} />
      <Sfx at={tip.cue('notices') + 10} name="click" volume={0.25} />
      <Sfx at={tip.cue('reach') - 6} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};
