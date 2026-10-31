import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { Mono } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { ease, glide, OUT } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

// «Making assistants (SDK)» in the documentation's menu (site/en/toc.yml), and the beat cue that lights each one.
const MENU: Array<[string, string?]> = [
  [t('Overview')], [t('Without code')], [t('With code')], [t('What the host gives'), 'host'], [t('Gestures'), 'gestures'],
  [t('Showcase')], [t('Signing and publishing')], [t('API reference'), 'api'],
];

/** Chapter «One more thing»: the documentation (drawn: its address and its SDK menu). */
export const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const k = glide(f, tip.cue('docs') - 10, 24);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ opacity: k, transform: `translateY(${(1 - k) * 50}px)` }}>
        <WinFrame width={1400} height={760} title={t('K-Desktop documentation')} style={{ left: 260, top: 150 }} icon={<Fluent name="globe_24_regular" size={18} color={C.sky} />}>
          <div style={{ margin: '6px 20px', height: 50, borderRadius: 25, background: 'rgba(255,255,255,0.07)', display: 'flex', alignItems: 'center', gap: 12, padding: '0 22px' }}>
            <Fluent name="lock_closed_24_regular" size={22} color={C.dim} />
            <Mono size={24}>kobayashi82.github.io/K-Desktop</Mono>
          </div>
          <div style={{ display: 'flex', padding: '20px 30px', gap: 40 }}>
            <div style={{ width: 460 }}>
              <div style={{ fontSize: 26, fontWeight: 800, color: C.text, marginBottom: 14 }}>{t('Making assistants (SDK)')}</div>
              {MENU.map(([name, cue]) => {
                const on = cue ? ease(f, tip.cue(cue) - 4, tip.cue(cue) + 8, OUT) : 0;
                return (
                  <div key={name} style={{ padding: '11px 18px', borderRadius: 10, fontSize: 25, color: on > 0.5 ? '#1a1306' : C.dim, fontWeight: on > 0.5 ? 800 : 500, background: on > 0.5 ? 'linear-gradient(180deg, #FFD970, #F2B807)' : undefined }}>{name}</div>
                );
              })}
            </div>
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 16, paddingTop: 50 }}>
              {[0.9, 0.7, 0.8, 0.5, 0.85, 0.6, 0.75].map((w, i) => <div key={i} style={{ height: 18, width: `${w * 100}%`, borderRadius: 9, background: 'rgba(255,255,255,0.08)' }} />)}
              <div style={{ marginTop: 20, padding: 22, borderRadius: 12, background: '#0c0c10' }}><Mono size={22} color={C.dim}>dotnet new kdassistant -n MyAssistant</Mono></div>
            </div>
          </div>
        </WinFrame>
      </div>
      {['host', 'gestures', 'api'].map(c => <Sfx key={c} at={tip.cue(c) - 4} name="blip" volume={0.12} />)}
    </AbsoluteFill>
  );
};
