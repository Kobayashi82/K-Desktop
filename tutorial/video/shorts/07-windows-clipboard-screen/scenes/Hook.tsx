import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { ShortAgenda, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, inOut, keys, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { AppId, Desk, lerpRect, Rect, Win, ZONES, ZoneOverlay } from './Desk';

/** Where the windows are before the layout puts them in place. */
export const MESS: Record<AppId, Rect> = {
  code: { x: 0.06, y: 0.1, w: 0.46, h: 0.58 },
  chrome: { x: 0.3, y: 0.28, w: 0.5, h: 0.6 },
  terminal: { x: 0.56, y: 0.06, w: 0.34, h: 0.42 },
};
export const ORDER: AppId[] = ['code', 'chrome', 'terminal'];

/** A messy desktop whose windows fly into their zones, the title, and what the video covers. */
export const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const mess = idea.cue('mess');
  const place = idea.cue('place');
  const desk = pop(f, idea.from - 4, { damping: 16 });
  const away = ease(f, idea.to - 14, idea.to + 10, IN);
  return (
    <AbsoluteFill>
      <Desk s={0.78 * (0.9 + 0.1 * desk) * (1 - 0.15 * away)} y={500 - away * 60} opacity={Math.min(1, desk * 1.3) * (1 - away)}>
        {ORDER.map((app, i) => {
          const k = pop(f, mess - 12 + i * 8, { damping: 13 });
          const go = ease(f, place + i * 5, place + i * 5 + 18, OUT);
          return <Win key={app} app={app} at={lerpRect(MESS[app], ZONES[i], go)} scale={0.85 + 0.15 * k} opacity={Math.min(1, k * 1.5)} />;
        })}
        <ZoneOverlay show={inOut(f, place - 8, place + 34, 8)} />
      </Desk>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Layouts'), icon: 'grid_24_filled', color: 'teal', at: plan.cue('layouts') },
          { label: t('Clipboard history'), icon: 'clipboard_text_edit_24_filled', color: 'brown', at: plan.cue('clipboard') },
          { label: t('Text from the screen'), icon: 'scan_text_24_filled', color: 'lime', at: plan.cue('screen') },
        ]} />
      )}
      {ORDER.map((app, i) => <Sfx key={app} at={mess - 12 + i * 8} name="pop" volume={0.18} />)}
      <Sfx at={place} name="whoosh-short" volume={0.3} />
      <Sfx at={place + 22} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};
