import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Sfx } from '../../../src/components/Sfx';
import { glide } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C } from '../../../src/theme';
import { Code, Terminal } from './Parts';

/** kaisign keygen once; from then on every build signs (site/en/sdk/publishing.md). */
const Key: React.FC = () => {
  const B = useBeats();
  const s = B('sign');
  return (
    <AbsoluteFill>
      <Terminal width={1200} height={300} style={{ left: 360, top: 260 }} lines={[
        { at: s.cue('keygen') - 8, cmd: 'kaisign keygen "Your name"' },
        { at: s.cue('signed') - 10, cmd: 'dotnet build' },
        { at: s.cue('signed') + 10, out: 'MyAssistant -> bin\\Debug\\…\\myassistant-1.0.0.kai' },
        { at: s.cue('signed') + 18, out: 'Signed: myassistant-1.0.0.kai (Your name, …)', color: C.success },
      ]} />
    </AbsoluteFill>
  );
};

/** What K-Desktop shows when it opens a signed package (assistant-install.png). */
const Check: React.FC = () => {
  const B = useBeats();
  const s = B('sign');
  const focus: Focus[] = [{ from: s.cue('check') - 4, to: s.to + 20, rect: [22, 348, 436, 62], label: t('Nobody changed it'), at: 'top' }];
  return (
    <>
      <CaptureShot name="assistant-install" frame focus={focus} shots={[{ at: s.cue('check') - 16, x: 240, y: 315, s: 1.5 }, { at: s.cue('check') - 12, dur: 26, x: 240, y: 330, s: 1.62 }]} />
      <Sfx at={s.cue('check') - 4} name="blip" volume={0.12} />
    </>
  );
};

/** A catalog, and «updates» in the manifest. */
const Catalog: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const c = B('catalog');
  const a = glide(f, c.from - 8, 22);
  const b = glide(f, c.cue('updates') - 8, 22);
  return (
    <AbsoluteFill>
      <div style={{ opacity: a, transform: `translateY(${(1 - a) * 40}px)` }}>
        <Code file="catalog.json" width={1500} size={23} style={{ left: 210, top: 110 }} lines={[
          '{',
          '  "catalog": 1,',
          '  "assistants": [',
          '    { "id": "myassistant",',
          '      "package": "https://github.com/me/my-assistants/releases/latest/download/myassistant.kai" }',
          '  ]',
          '}',
        ]} lit={[{ from: c.cue('catalog') + 10, to: c.cue('updates') - 4, lines: [3, 4] }]} />
      </div>
      <div style={{ opacity: b, transform: `translateY(${(1 - b) * 40}px)` }}>
        <Code file="assistant.json" width={1500} size={23} style={{ left: 210, top: 560 }} lines={[
          '  "version": "1.1.0",',
          '  "updates": "https://github.com/me/my-assistants/releases/latest/download/myassistant.kai",',
          '  "changes": { "en": "Jumps higher and talks faster." }',
        ]} lit={[{ from: c.cue('updates') - 4, to: c.to + 30, lines: [1, 1] }]} />
      </div>
      <Sfx at={c.from - 8} name="whoosh-short" volume={0.18} />
      <Sfx at={c.cue('updates') - 8} name="whoosh-short" volume={0.18} />
    </AbsoluteFill>
  );
};

/** Chapter «Share it». */
export const Share: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const s = B('sign');
  return (
    <AbsoluteFill>
      <Part from={0} to={s.cue('check') - 10} first><Key /></Part>
      <Part from={s.cue('check') - 10} to={B('catalog').from - 6}><Check /></Part>
      <Part from={B('catalog').from - 6} to={durationInFrames} last><Catalog /></Part>
    </AbsoluteFill>
  );
};
