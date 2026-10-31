import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Sfx } from '../../../src/components/Sfx';
import { Showcase } from '../../../src/components/Showcase';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';

export const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

const OPEN = [{ at: 0, x: 960, y: 540, s: 0.96, rx: 6 }, { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 }];

/** Settings › Assistant › General (assistant-settings.png): the module switch. */
const Module: React.FC = () => {
  const B = useBeats();
  const m = B('module');
  const focus: Focus[] = [
    { from: m.cue('settings') - 4, to: m.cue('on') - 4, rect: [261, 1017, 266, 46] },
    { from: m.cue('on') - 4, to: m.cue('off') - 4, rect: [575, 163, 950, 38], label: t('Turn on the Assistant module'), at: 'bottom' },
    { from: m.cue('off') - 4, to: m.to + 20, rect: [575, 128, 790, 20], label: t('Off: nothing of it is loaded'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="assistant-settings" focus={focus} shots={[...OPEN, { at: m.cue('on') - 16, dur: 26, x: 1050, y: 200, s: 1.6 }]} />
      {blips(focus)}
      <Sfx at={m.cue('on') + 4} name="click" volume={0.22} />
    </>
  );
};

/** Settings › Assistant › Catalog (assistant-catalog.png). */
const Catalog: React.FC = () => {
  const B = useBeats();
  const c = B('catalog');
  const focus: Focus[] = [
    { from: c.cue('catalog') - 4, to: c.cue('lander') - 4, rect: [261, 832, 266, 46] },
    { from: c.cue('lander') - 4, to: c.cue('kobii') - 4, rect: [581, 406, 940, 95] },
    { from: c.cue('kobii') - 4, to: c.cue('kumo') - 4, rect: [581, 200, 940, 95] },
    { from: c.cue('kumo') - 4, to: c.to + 20, rect: [581, 303, 940, 95] },
  ];
  return (
    <>
      <CaptureShot name="assistant-catalog" focus={focus}
        shots={[{ at: c.from - 10, x: 960, y: 540, s: 1.0 }, { at: c.cue('lander') - 16, dur: 26, x: 1050, y: 350, s: 1.45 }]} />
      {blips(focus)}
    </>
  );
};

/** The card shown before installing (assistant-install.png, a 480×631 dialog). */
const Card: React.FC = () => {
  const B = useBeats();
  const c = B('card');
  const focus: Focus[] = [
    { from: c.cue('what') - 4, to: c.cue('signed') - 4, rect: [20, 205, 440, 110], label: t('What it can do'), at: 'top' },
    { from: c.cue('signed') - 4, to: c.cue('keys') - 4, rect: [22, 348, 436, 62], label: t('Signed and unchanged'), at: 'top' },
    { from: c.cue('keys') - 4, to: c.to + 20, rect: [20, 425, 440, 135], label: t('Its hotkeys, as commands'), at: 'top' },
  ];
  return (
    <>
      <CaptureShot name="assistant-install" frame focus={focus}
        shots={[{ at: c.from - 10, x: 240, y: 315, s: 1.5 }, { at: c.cue('card') - 4, dur: 30, x: 240, y: 315, s: 1.62 }]} />
      {blips(focus)}
      <Sfx at={c.cue('card') - 8} name="whoosh-short" volume={0.2} />
    </>
  );
};

/** Settings › Assistant › Assistants (assistant-list.png): «Put on the desktop», several at once. */
const Desktop: React.FC = () => {
  const B = useBeats();
  const d = B('desktop');
  const focus: Focus[] = [
    { from: d.cue('list') - 4, to: d.cue('put') - 4, rect: [261, 881, 266, 46] },
    { from: d.cue('put') - 4, to: d.cue('several') - 4, rect: [1253, 352, 165, 36], label: t('Put on the desktop'), at: 'bottom' },
    { from: d.cue('several') - 4, to: d.to + 20, rect: [581, 150, 940, 250], label: t('Several at once'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="assistant-list" focus={focus}
        shots={[{ at: d.from - 10, x: 960, y: 540, s: 1.0 }, { at: d.cue('put') - 16, dur: 26, x: 1050, y: 300, s: 1.5 }]} />
      {blips(focus)}
      <Sfx at={d.cue('put') + 2} name="click" volume={0.22} />
    </>
  );
};

/** «Try» in the Lander's settings (lander-try.png), its test desktop playing the real clip of «Wave». */
const Try: React.FC = () => {
  const B = useBeats();
  const x = B('try');
  const focus: Focus[] = [
    { from: x.cue('try') - 4, to: x.cue('test') - 4, rect: [853, 217, 65, 28], label: t('Try'), at: 'bottom' },
    { from: x.cue('test') - 4, to: x.cue('what') - 4, rect: [562, 298, 978, 552], label: t('A test desktop'), at: 'top' },
    { from: x.cue('what') - 4, to: x.to + 30, rect: [600, 862, 700, 38], label: t('What it does'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="lander-try" focus={focus}
        shots={[{ at: x.from - 10, x: 960, y: 540, s: 1.0 }, { at: x.cue('test') - 16, dur: 26, x: 1050, y: 600, s: 1.3 }]}>
        <div style={{ position: 'absolute', left: 562, top: 298 }}>
          <Showcase clip="Lander/lander.wave" width={978} from={x.from - 10} />
        </div>
      </CaptureShot>
      {blips(focus)}
    </>
  );
};

/** Chapter «Add an assistant». */
export const Install: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const at = (id: string) => B(id).from - 6;
  return (
    <AbsoluteFill>
      <Part from={0} to={at('catalog')} first><Module /></Part>
      <Part from={at('catalog')} to={at('card')}><Catalog /></Part>
      <Part from={at('card')} to={at('desktop')}><Card /></Part>
      <Part from={at('desktop')} to={at('try')}><Desktop /></Part>
      <Part from={at('try')} to={durationInFrames} last><Try /></Part>
    </AbsoluteFill>
  );
};
