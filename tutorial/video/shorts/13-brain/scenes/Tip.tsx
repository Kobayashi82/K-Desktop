import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { Bubble } from '../../../src/scenes/assistants/Inside';
import { C, SANS } from '../../../src/theme';

/** Chapter «One more thing»: the «Assistant: brain» action, or just saying «free the brain». */
export const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const card = glide(f, tip.cue('action') - 12, 22);
  const say = tip.cue('say');
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{
        position: 'absolute', left: 960, top: 300, width: 1000, padding: '28px 34px', borderRadius: 26, boxSizing: 'border-box', transform: `translate(-50%, -50%) translateY(${(1 - card) * 60}px)`, opacity: card,
        background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
          <KdIcon icon="brain_circuit_24_filled" color="purple" size={64} />
          <div>
            <div style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{t('Assistant: brain')}</div>
            <div style={{ fontSize: 20, color: C.dim, marginTop: 4 }}>{t('Takes the AI model out of the graphics card (before a game or a render) or loads it now.')}</div>
          </div>
        </div>
        <div style={{ marginTop: 20, display: 'inline-block', padding: '10px 18px', borderRadius: 10, background: C.accentSoft, color: C.accentText, fontSize: 22, fontWeight: 600 }}>{t('Take the model out of the graphics card')}</div>
      </div>
      <Bubble user text={t('Free the brain')} show={pop(f, say - 6, { damping: 14 })} x={1500} y={620} size={36} />
      <Bubble text={t('Done, the model is out of the graphics card. I’ll load it again when you talk to me.')} show={pop(f, say + 8, { damping: 14 })} x={360} y={800} size={30} width={900} />
      <Sfx at={tip.cue('action') - 12} name="whoosh-short" volume={0.22} />
      <Sfx at={say - 6} name="pop" volume={0.2} />
      <Sfx at={say + 8} name="pop-high" volume={0.2} />
    </AbsoluteFill>
  );
};
