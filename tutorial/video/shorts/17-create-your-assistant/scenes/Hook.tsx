import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Mono } from '../../../src/components/Keys';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { PipSprite } from '../../../src/scenes/assistants/Meet';

/** Pictures and JSON, or a .NET project; the title and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const pics = pop(f, idea.cue('pictures') - 8, { damping: 13 });
  const code = pop(f, idea.cue('code') - 8, { damping: 13 });
  const away = ease(f, idea.to - 10, idea.to + 10, IN);
  const tile = (k: number, x: number, children: React.ReactNode) => (
    <div style={{
      position: 'absolute', left: x, top: 450, transform: `translate(-50%, -50%) scale(${k})`, opacity: Math.min(1, k * 1.4),
      width: 620, height: 420, borderRadius: 30, background: 'rgba(30,30,36,0.94)', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)',
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 24, fontFamily: SANS,
    }}>{children}</div>
  );
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - away }}>
        {tile(pics, 610, <>
          <div style={{ display: 'flex', gap: 6 }}>
            {['idle', 'talk', 'happy'].map(a => <PipSprite key={a} anim={a} size={150} fps={6} />)}
          </div>
          <Mono size={24} color={C.dim}>{t('frames/*.png + character.json')}</Mono>
          <span style={{ fontSize: 34, fontWeight: 700, color: C.text }}>{t('Without code')}</span>
        </>)}
        {tile(code, 1310, <>
          <div style={{ padding: '22px 28px', borderRadius: 16, background: '#1e1e1e', border: `1px solid ${C.line2}`, textAlign: 'left' }}>
            <Mono size={24} color="#569CD6">{t('public sealed class')}</Mono> <Mono size={24} color="#4EC9B0">MyAssistant</Mono><br />
            <Mono size={24} color="#D4D4D4">{'    : '}</Mono><Mono size={24} color="#4EC9B0">IAssistant</Mono>
          </div>
          <Mono size={24} color={C.dim}>{t('.NET 10 + KDesktop.Assistant.Sdk')}</Mono>
          <span style={{ fontSize: 34, fontWeight: 700, color: C.text }}>{t('With code')}</span>
        </>)}
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 2} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Without code'), icon: 'image_multiple_24_filled', color: 'teal', at: plan.cue('nocode') },
          { label: t('With code'), icon: 'code_24_filled', color: 'purple', at: plan.cue('code') },
          { label: t('Try it'), icon: 'play_24_filled', color: 'green', at: plan.cue('try') },
          { label: t('Share it'), icon: 'share_24_filled', color: 'orange', at: plan.cue('share') },
        ]} />
      )}
      <Sfx at={idea.cue('pictures') - 8} name="pop" volume={0.2} />
      <Sfx at={idea.cue('code') - 8} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};
