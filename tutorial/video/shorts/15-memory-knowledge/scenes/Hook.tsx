import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { Bubble } from '../../../src/scenes/assistants/Inside';
import { C, SANS } from '../../../src/theme';

/** Something said once is remembered; documents answer a question. Then the title and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const away = ease(f, idea.to - 12, idea.to + 8, IN);
  const mem = pop(f, idea.cue('remembers') - 4, { damping: 14 });
  const docs = pop(f, idea.cue('documents') - 6, { damping: 14 });
  const ans = pop(f, idea.cue('answers') - 4, { damping: 14 });
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - away }}>
        <Bubble user text={t('Remember that I’m allergic to seafood')} show={pop(f, idea.cue('once') - 10, { damping: 14 })} x={900} y={300} size={32} />
        <div style={{
          position: 'absolute', left: 160, top: 390, width: 740, display: 'flex', alignItems: 'center', gap: 16, padding: '18px 22px', borderRadius: 18,
          background: 'rgba(30,30,36,0.95)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, mem * 1.4), transform: `translateY(${(1 - mem) * 30}px)`,
        }}>
          <KdIcon icon="brain_circuit_24_filled" color="purple" size={50} />
          <div>
            <div style={{ fontSize: 26, fontWeight: 600, color: C.text }}>{t('Alex is allergic to seafood.')}</div>
            <div style={{ fontSize: 18, color: C.dim }}>{t('You asked · today')}</div>
          </div>
        </div>
        <div style={{ position: 'absolute', left: 1180, top: 250, display: 'flex', gap: 14, transform: `scale(${docs})`, opacity: Math.min(1, docs * 1.4) }}>
          {['document_pdf_24_filled', 'document_text_24_filled', 'document_pdf_24_filled'].map((i, k) => <KdIcon key={k} icon={i} color={['red', 'sky', 'red'][k]} size={110} style={{ transform: `rotate(${(k - 1) * 8}deg)` }} />)}
        </div>
        <div style={{
          position: 'absolute', left: 1060, top: 450, width: 700, padding: '20px 24px', borderRadius: 18, background: 'white', color: '#141418',
          boxShadow: '0 20px 50px rgba(0,0,0,0.5)', opacity: Math.min(1, ans * 1.4), transform: `scale(${0.85 + 0.15 * ans})`,
        }}>
          <div style={{ fontSize: 28, fontWeight: 700 }}>{t('October’s electricity bill was 64.20 €.')}</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 10, fontSize: 19, color: '#555' }}>
            <Fluent name="document_pdf_24_filled" size={22} color="#d9363e" /> electricity-october.pdf · {t('page 1')}
          </div>
        </div>
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Memory and notes'), icon: 'brain_circuit_24_filled', color: 'purple', at: plan.cue('memory') },
          { label: t('What it knows'), icon: 'library_24_filled', color: 'orange', at: plan.cue('knows') },
          { label: t('Drop files'), icon: 'document_arrow_down_24_filled', color: 'teal', at: plan.cue('drop') },
        ]} />
      )}
      <Sfx at={idea.cue('once') - 10} name="pop" volume={0.2} />
      <Sfx at={idea.cue('remembers') - 4} name="chime" volume={0.18} />
      <Sfx at={idea.cue('documents') - 6} name="pop" volume={0.2} />
      <Sfx at={idea.cue('answers') - 4} name="pop-high" volume={0.2} />
    </AbsoluteFill>
  );
};
