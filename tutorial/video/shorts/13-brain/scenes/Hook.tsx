import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** A brain inside your PC that nothing leaves, the title, and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const brain = pop(f, idea.cue('ai') - 8, { damping: 12, stiffness: 120 });
  const pc = pop(f, idea.cue('pc') - 8, { damping: 15 });
  const badge = pop(f, idea.cue('stays') - 6, { damping: 13 });
  const away = ease(f, idea.to - 14, idea.to + 8, IN);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - away, transform: `scale(${1 - 0.1 * away})` }}>
        <div style={{ position: 'absolute', left: 960, top: 450, transform: `translate(-50%, -50%) scale(${0.8 + 0.2 * pc})`, opacity: Math.min(1, pc * 1.4) }}>
          <div style={{ width: 640, height: 400, borderRadius: 26, border: `8px solid ${C.line2}`, background: 'rgba(20,20,26,0.7)', boxShadow: '0 40px 100px rgba(0,0,0,0.5)' }} />
          <div style={{ width: 120, height: 60, margin: '0 auto', background: 'rgba(255,255,255,0.12)', clipPath: 'polygon(20% 0, 80% 0, 100% 100%, 0 100%)' }} />
        </div>
        <div style={{ position: 'absolute', left: 960, top: 430, transform: `translate(-50%, -50%) scale(${brain})` }}>
          <KdIcon icon="brain_circuit_24_filled" color="purple" size={220} glow={0.6 + 0.3 * Math.sin(f / 9)} />
        </div>
        <div style={{
          position: 'absolute', left: 960, top: 770, transform: `translate(-50%, -50%) scale(${badge})`, opacity: Math.min(1, badge * 1.4),
          display: 'flex', alignItems: 'center', gap: 12, padding: '14px 28px', borderRadius: 40, background: 'rgba(30,30,36,0.94)', border: `1.5px solid ${C.success}`,
        }}>
          <Fluent name="lock_closed_24_filled" size={34} color={C.success} />
          <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 700, color: C.text }}>{t('On this PC')}</span>
        </div>
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Choose a model'), icon: 'brain_circuit_24_filled', color: 'purple', at: plan.cue('choose') },
          { label: t('Other servers'), icon: 'server_24_filled', color: 'blue', at: plan.cue('servers') },
          { label: t('The graphics card'), icon: 'games_24_filled', color: 'green', at: plan.cue('gpu') },
        ]} />
      )}
      <Sfx at={idea.cue('ai') - 8} name="impact" volume={0.22} />
      <Sfx at={idea.cue('pc') - 8} name="whoosh-short" volume={0.2} />
      <Sfx at={idea.cue('stays') - 6} name="pop-high" volume={0.2} />
    </AbsoluteFill>
  );
};
