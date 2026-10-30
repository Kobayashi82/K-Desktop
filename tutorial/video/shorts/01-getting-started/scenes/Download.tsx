import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../../../src/components/Camera';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Cursor } from '../../../src/components/Cursor';
import { Taskbar, Wallpaper, Lines } from '../../../src/components/Desktop';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { Mono } from '../../../src/components/Keys';
import { Osd } from '../../../src/components/Osd';
import { Sfx } from '../../../src/components/Sfx';
import { Callout } from '../../../src/components/Ui';
import { ease, glide, inOut, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { C, MONO, SANS } from '../../../src/theme';

/** The program as a file: K-Desktop's icon and its name. */
const ExeFile: React.FC<{ size?: number; label?: boolean; glow?: number }> = ({ size = 96, label = true, glow = 0 }) => (
  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: size * 0.12 }}>
    <Img src={staticFile('brand/icon-192.png')} style={{ width: size, height: size, filter: `drop-shadow(0 ${size * 0.1}px ${size * 0.3}px rgba(224,168,21,${0.3 + 0.4 * glow}))` }} />
    {label && <Mono size={size * 0.2} color={C.text}>KDesktop.exe</Mono>}
  </div>
);

const Chip: React.FC<{ icon: string; text: string; show: number }> = ({ icon, text, show }) => (
  <div style={{
    display: 'flex', alignItems: 'center', gap: 12, padding: '12px 24px', borderRadius: 40, background: 'rgba(30,30,36,0.92)', border: '1.5px solid rgba(242,193,74,0.45)',
    opacity: Math.min(1, show * 1.4), transform: `translateY(${(1 - show) * 40}px) scale(${0.8 + 0.2 * show})`, boxShadow: '0 16px 40px rgba(0,0,0,0.4)',
  }}>
    <Fluent name={icon} size={30} color={C.accentText} />
    <span style={{ fontFamily: SANS, fontSize: 32, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{text}</span>
  </div>
);

/** «A single portable program for Windows 10 and 11». */
const ProgramPart: React.FC<{ get: BeatTimes }> = ({ get }) => {
  const f = useCurrentFrame();
  const file = pop(f, 4, { damping: 12, stiffness: 110 });
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 400, transform: `translate(-50%, -50%) scale(${file})` }}>
        <ExeFile size={230} glow={0.6} />
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 720, display: 'flex', justifyContent: 'center', gap: 30 }}>
        <Chip icon="document_one_page_24_regular" text={t('One portable file')} show={pop(f, get.cue('single') - 4, { damping: 14 })} />
        <Chip icon="window_24_regular" text="Windows 10 · 11" show={pop(f, get.cue('windows') - 4, { damping: 14 })} />
      </div>
      <Sfx at={4} name="impact" volume={0.25} />
      <Sfx at={get.cue('single') - 4} name="pop" volume={0.25} />
      <Sfx at={get.cue('windows') - 4} name="pop" volume={0.25} />
    </AbsoluteFill>
  );
};

const BROWSER = { x: 210, y: 70, w: 1500, h: 900 };

/** The project's page in a browser: Releases, the latest version and its download. */
const ReleasePart: React.FC<{ get: BeatTimes; from: number; to: number }> = ({ get, from, to }) => {
  const f = useCurrentFrame();
  const releasesAt = get.cue('releases');
  const exeAt = get.cue('exe');
  const onReleases = f >= releasesAt - 2;
  const card = glide(f, releasesAt, 16);
  const download = ease(f, exeAt + 2, exeAt + 30, OUT);
  const flyout = inOut(f, exeAt, to + 30, 8);
  const tab = (label: string, active: boolean, count?: string) => (
    <div style={{ position: 'relative', display: 'flex', alignItems: 'center', gap: 8, padding: '14px 4px', fontSize: 19, fontWeight: active ? 700 : 500, color: active ? C.text : C.dim }}>
      {label}{count && <span style={{ fontSize: 14, padding: '1px 8px', borderRadius: 10, background: C.chip }}>{count}</span>}
      {active && <div style={{ position: 'absolute', left: -4, right: -4, bottom: -1, height: 3, borderRadius: 2, background: C.accentText }} />}
    </div>
  );
  // Stage points: the Releases tab and the KDesktop.exe asset.
  const tabAt = { x: BROWSER.x + 330, y: BROWSER.y + 186 };
  const assetAt = { x: BROWSER.x + 230, y: BROWSER.y + 642 };
  return (
    <AbsoluteFill>
      <Camera shots={[
        { at: from, x: 960, y: 520, s: 0.96, ry: -6 },
        { at: from, dur: 30, x: 960, y: 520, s: 1.0, ry: 0 },
        { at: releasesAt + 10, dur: 30, x: 960, y: 590, s: 1.12 },
        { at: exeAt + 30, dur: 40, x: 960, y: 520, s: 1.08 },
      ]}>
        <div style={{ position: 'absolute', left: BROWSER.x, top: BROWSER.y, transform: `translateY(${(1 - glide(f, from, 24)) * 80}px)` }}>
          <WinFrame width={BROWSER.w} height={BROWSER.h} title={t('K-Desktop · Releases')} bg="#17181c" icon={<Fluent name="globe_24_regular" size={18} color={C.text} />}>
            <div style={{ fontFamily: SANS, color: C.text }}>
              {/* Address bar */}
              <div style={{ height: 50, margin: '4px 16px 0', borderRadius: 25, background: 'rgba(255,255,255,0.07)', display: 'flex', alignItems: 'center', gap: 12, padding: '0 20px' }}>
                <Fluent name="lock_closed_24_regular" size={18} color={C.dim} />
                <span style={{ fontFamily: MONO, fontSize: 19, color: C.text }}>github.com/Kobayashi82/K-Desktop{onReleases ? <span style={{ color: C.accentText }}>/releases</span> : ''}</span>
              </div>
              {/* Project header */}
              <div style={{ padding: '26px 44px 0', borderBottom: `1px solid ${C.line}` }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 14, fontSize: 26 }}>
                  <Img src={staticFile('brand/icon-64.png')} style={{ width: 34, height: 34 }} />
                  <span style={{ color: C.dim }}>Kobayashi82</span><span style={{ color: C.faint }}>/</span><span style={{ fontWeight: 700 }}>K-Desktop</span>
                </div>
                <div style={{ display: 'flex', gap: 34, marginTop: 14 }}>
                  {tab(t('Code'), !onReleases)}
                  {tab(t('Releases'), onReleases, '1')}
                  {tab(t('Wiki'), false)}
                </div>
              </div>
              {/* Before: the project's front page; after: the latest release */}
              {!onReleases ? (
                <div style={{ padding: '34px 44px', display: 'flex', gap: 40 }}>
                  <div style={{ flex: 1 }}><Lines count={12} width={820} gap={20} height={11} /></div>
                  <div style={{ width: 360 }}><Lines count={6} width={300} gap={20} height={11} seed={4} /></div>
                </div>
              ) : (
                <div style={{ margin: '30px 44px', padding: 30, borderRadius: 16, border: `1px solid ${C.line2}`, background: 'rgba(255,255,255,0.03)', opacity: card, transform: `translateY(${(1 - card) * 30}px)` }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
                    <span style={{ fontSize: 36, fontWeight: 800 }}>K-Desktop 5.0.451</span>
                    <span style={{ fontSize: 16, fontWeight: 700, color: C.success, border: `1.5px solid ${C.success}`, padding: '3px 12px', borderRadius: 14 }}>{t('Latest')}</span>
                  </div>
                  <div style={{ margin: '18px 0 26px' }}><Lines count={2} width={900} gap={16} height={10} seed={7} /></div>
                  <div style={{ fontSize: 22, fontWeight: 700, marginBottom: 12 }}>{t('Assets')}</div>
                  {[
                    { name: 'KDesktop.exe', size: '224 MB', main: true },
                    { name: 'kdesktop.json', size: '1 KB' },
                    { name: t('Source code (zip)'), size: '' },
                  ].map((a, i) => {
                    const hot = a.main ? inOut(f, exeAt - 14, exeAt + 30, 6) : 0;
                    return (
                      <div key={i} style={{
                        display: 'flex', alignItems: 'center', gap: 16, height: 62, padding: '0 18px', borderRadius: 10, marginBottom: 6,
                        background: hot > 0 ? `rgba(242,193,74,${0.14 * hot})` : 'rgba(255,255,255,0.03)', border: `1px solid ${hot > 0 ? `rgba(242,193,74,${0.6 * hot})` : 'transparent'}`,
                      }}>
                        <Fluent name={i === 2 ? 'folder_zip_24_regular' : 'document_24_regular'} size={26} color={a.main ? C.accentText : C.dim} />
                        <span style={{ fontSize: 22, fontWeight: a.main ? 700 : 500, color: a.main ? C.accentText : C.text }}>{a.name}</span>
                        <span style={{ marginLeft: 'auto', fontSize: 18, color: C.dim }}>{a.size}</span>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          </WinFrame>
          {/* The browser's downloads */}
          <div style={{
            position: 'absolute', right: 30, top: 110, width: 420, padding: 22, borderRadius: 14, background: '#26272d', border: `1px solid ${C.line2}`,
            boxShadow: '0 20px 50px rgba(0,0,0,0.55)', opacity: flyout, transform: `translateY(${(1 - flyout) * -20}px)`, fontFamily: SANS, color: C.text,
          }}>
            <div style={{ fontSize: 18, fontWeight: 700, marginBottom: 14 }}>{t('Downloads')}</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
              <Img src={staticFile('brand/icon-64.png')} style={{ width: 40, height: 40 }} />
              <div style={{ flex: 1 }}>
                <div style={{ fontSize: 18, fontWeight: 600 }}>KDesktop.exe</div>
                <div style={{ height: 5, borderRadius: 3, background: 'rgba(255,255,255,0.12)', marginTop: 8 }}>
                  <div style={{ width: `${download * 100}%`, height: '100%', borderRadius: 3, background: download >= 1 ? C.success : C.accent }} />
                </div>
                <div style={{ fontSize: 14, color: C.dim, marginTop: 6 }}>{download >= 1 ? t('224 MB · Done') : `${Math.round(224 * download)} MB / 224 MB`}</div>
              </div>
            </div>
          </div>
        </div>
        <Cursor path={[
          { at: from, x: 1200, y: 760 },
          { at: releasesAt - 24, x: tabAt.x, y: tabAt.y, dur: 18 },
          { at: exeAt - 26, x: assetAt.x, y: assetAt.y, dur: 20 },
          { at: exeAt + 20, x: 1400, y: 820, dur: 24 },
        ]} clicks={[{ at: releasesAt - 4 }, { at: exeAt - 3 }]} />
      </Camera>
      <Sfx at={releasesAt - 4} name="click" volume={0.3} />
      <Sfx at={exeAt - 3} name="click" volume={0.3} />
      <Sfx at={exeAt + 30} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};

const FOLDER = { x: 360, y: 150, w: 1200, h: 700 };

/** No installer: into a folder of your own, and open it. */
const FolderPart: React.FC<{ run: BeatTimes }> = ({ run }) => {
  const f = useCurrentFrame();
  const folderAt = run.cue('folder');
  const openAt = run.cue('open');
  const from = run.from - 4;
  const drop = pop(f, folderAt - 8, { damping: 13, stiffness: 140 });
  const pulse = inOut(f, openAt - 4, openAt + 20, 4);
  const file = { x: FOLDER.x + 120, y: FOLDER.y + 210 };
  return (
    <AbsoluteFill>
      <Camera shots={[{ at: from, x: 960, y: 500, s: 1.0 }, { at: folderAt - 20, dur: 40, x: 760, y: 470, s: 1.2 }]}>
        <Wallpaper />
        <Taskbar apps={[{ icon: 'folder_24_filled', color: 'yellow', active: true }, { icon: 'globe_24_filled', color: 'blue' }]} />
        <div style={{ position: 'absolute', left: FOLDER.x, top: FOLDER.y, transform: `scale(${0.94 + 0.06 * glide(f, from, 20)})`, opacity: glide(f, from, 14) }}>
          <WinFrame width={FOLDER.w} height={FOLDER.h} title="K-Desktop" bg="#1b1c21" icon={<KdIcon icon="folder_24_filled" color="yellow" size={20} />}>
            <div style={{ height: 44, margin: '4px 16px', borderRadius: 8, background: 'rgba(255,255,255,0.06)', display: 'flex', alignItems: 'center', gap: 10, padding: '0 16px' }}>
              <Fluent name="folder_24_regular" size={18} color={C.dim} />
              <span style={{ fontFamily: SANS, fontSize: 18, color: C.text }}>{t('This PC')} › D: › {t('Tools')} › K-Desktop</span>
            </div>
            <div style={{ display: 'flex', height: FOLDER.h - 110 }}>
              <div style={{ width: 210, padding: 20, borderRight: `1px solid ${C.line}` }}><Lines count={8} width={150} gap={22} height={10} /></div>
              <div style={{ flex: 1, position: 'relative' }}>
                {drop <= 0.01 && <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: SANS, fontSize: 20, color: C.faint }}>{t('This folder is empty.')}</div>}
              </div>
            </div>
          </WinFrame>
        </div>
        <div style={{
          position: 'absolute', left: file.x, top: file.y, padding: 14, borderRadius: 10, background: pulse > 0 ? `rgba(90,169,255,${0.25 * pulse + 0.1})` : 'transparent',
          transform: `translateY(${(1 - drop) * -260}px) scale(${(0.5 + 0.5 * drop) * (1 + 0.05 * pulse)})`, opacity: Math.min(1, drop * 2),
        }}>
          <ExeFile size={96} glow={pulse} />
        </div>
        <Cursor path={[
          { at: from, x: 1300, y: 760 },
          { at: openAt - 24, x: file.x + 62, y: file.y + 66, dur: 18 },
        ]} clicks={[{ at: openAt - 5 }, { at: openAt }]} />
      </Camera>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 120, display: 'flex', justifyContent: 'center', gap: 26, opacity: 1 - ease(f, folderAt - 10, folderAt + 6) }}>
        <Chip icon="box_dismiss_24_regular" text={t('No installer')} show={pop(f, run.cue('noinstall') - 4, { damping: 14 })} />
        <Chip icon="checkmark_circle_24_regular" text={t('Nothing else to install')} show={pop(f, run.seg(0) + 30, { damping: 14 })} />
      </div>
      <Sfx at={run.cue('noinstall') - 4} name="pop" volume={0.25} />
      <Sfx at={folderAt - 8} name="whoosh-down" volume={0.3} />
      <Sfx at={openAt - 5} name="click" volume={0.3} />
      <Sfx at={openAt} name="click" volume={0.3} />
    </AbsoluteFill>
  );
};

/** If SmartScreen steps in: More info, Run anyway. */
const SmartPart: React.FC<{ smart: BeatTimes }> = ({ smart }) => {
  const f = useCurrentFrame();
  const moreAt = smart.cue('more');
  const anywayAt = smart.cue('anyway');
  const from = smart.from - 4;
  const box = pop(f, from + 2, { damping: 16, stiffness: 150 });
  const more = f >= moreAt + 2;
  const DIALOG = { x: 560, y: 220, w: 800, h: 560 };
  const linkAt = { x: DIALOG.x + 100, y: DIALOG.y + 300 };
  const buttonAt = { x: DIALOG.x + 470, y: DIALOG.y + DIALOG.h - 52 };
  return (
    <AbsoluteFill>
      <Camera shots={[{ at: from, x: 960, y: 520, s: 1.0 }, { at: moreAt + 10, dur: 30, x: 960, y: 560, s: 1.08 }]}>
        <Wallpaper />
        <Taskbar apps={[{ icon: 'folder_24_filled', color: 'yellow', active: true }, { icon: 'globe_24_filled', color: 'blue' }]} />
        <div style={{
          position: 'absolute', left: DIALOG.x, top: DIALOG.y, width: DIALOG.w, height: DIALOG.h, background: '#1f5fa8', borderRadius: 4, overflow: 'hidden',
          boxShadow: '0 30px 80px rgba(0,0,0,0.6)', transform: `scale(${0.9 + 0.1 * box})`, opacity: Math.min(1, box * 1.5), fontFamily: SANS, color: 'white',
        }}>
          <div style={{ padding: '40px 44px 0' }}>
            <div style={{ fontSize: 44, fontWeight: 300 }}>{t('Windows protected your PC')}</div>
            <div style={{ marginTop: 26 }}><Lines count={2} width={640} gap={16} height={11} color="rgba(255,255,255,0.45)" /></div>
            <div style={{ marginTop: 34, fontSize: 22, textDecoration: 'underline', fontWeight: 600, opacity: more ? 0.5 : 1 }}>{t('More info')}</div>
            {more && (
              <div style={{ marginTop: 26, fontSize: 22, lineHeight: 1.7, opacity: ease(f, moreAt + 2, moreAt + 12) }}>
                {t('App')}: <b>KDesktop.exe</b><br />{t('Publisher')}: {t('Unknown publisher')}
              </div>
            )}
          </div>
          <div style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: 104, background: '#1a4f8e', display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 16, padding: '0 36px' }}>
            {more && (
              <div style={{
                padding: '12px 30px', fontSize: 21, border: '2px solid rgba(255,255,255,0.85)', background: `rgba(255,255,255,${0.12 + 0.25 * inOut(f, anywayAt - 10, anywayAt + 20, 4)})`,
                opacity: ease(f, moreAt + 2, moreAt + 12),
              }}>{t('Run anyway')}</div>
            )}
            <div style={{ padding: '12px 30px', fontSize: 21, background: 'rgba(255,255,255,0.85)', color: '#1a3a66' }}>{t("Don't run")}</div>
          </div>
        </div>
        <Cursor path={[
          { at: from, x: 1250, y: 860 },
          { at: moreAt - 22, x: linkAt.x, y: linkAt.y, dur: 16 },
          { at: anywayAt - 22, x: buttonAt.x, y: buttonAt.y, dur: 16 },
        ]} clicks={[{ at: moreAt - 3 }, { at: anywayAt - 2 }]} />
      </Camera>
      <Callout show={inOut(f, smart.cue('smart') - 4, smart.to + 20, 8)} x={960} y={140} align="center" gold={false} size={28}>{t('Only if Windows asks')}</Callout>
      <Sfx at={from + 2} name="blip-low" volume={0.25} />
      <Sfx at={moreAt - 3} name="click" volume={0.3} />
      <Sfx at={anywayAt - 2} name="click" volume={0.3} />
    </AbsoluteFill>
  );
};

/** It keeps itself up to date: a new version, installed while you're not using it. */
const UpdatePart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const newAt = b.cue('new');
  const restartAt = b.cue('restarts');
  const card = pop(f, b.from - 2, { damping: 14, stiffness: 130 });
  const fresh = pop(f, newAt - 4, { damping: 12, stiffness: 160 });
  const spin = ease(f, restartAt - 6, restartAt + 30);
  const done = f > restartAt + 30;
  return (
    <AbsoluteFill>
      <div style={{
        position: 'absolute', left: 960, top: 430, transform: `translate(-50%, -50%) scale(${card})`, width: 760, padding: '40px 46px', borderRadius: 30, boxSizing: 'border-box',
        background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: '1.5px solid rgba(242,193,74,0.35)', boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
        display: 'flex', alignItems: 'center', gap: 34, fontFamily: SANS,
      }}>
        <div style={{ position: 'relative', width: 150, height: 150 }}>
          <Img src={staticFile('brand/icon-192.png')} style={{ width: 150, height: 150 }} />
          <div style={{
            position: 'absolute', right: -14, bottom: -14, width: 64, height: 64, borderRadius: 32, background: done ? C.success : C.accentText,
            display: 'flex', alignItems: 'center', justifyContent: 'center', transform: `scale(${fresh}) rotate(${spin * 720}deg)`, boxShadow: '0 6px 20px rgba(0,0,0,0.4)',
          }}>
            <Fluent name={done ? 'checkmark_24_filled' : 'arrow_sync_24_filled'} size={36} color="#1a1306" />
          </div>
        </div>
        <div>
          <div style={{ fontSize: 50, fontWeight: 800, color: C.text }}>K-Desktop</div>
          <div style={{ fontSize: 30, fontWeight: 600, color: C.dim, marginTop: 6, fontVariantNumeric: 'tabular-nums' }}>
            {done ? '5.0.452' : '5.0.451'}
            {!done && fresh > 0.05 && <span style={{ marginLeft: 16, fontSize: 22, color: C.accentText, background: C.accentSoft, padding: '4px 14px', borderRadius: 14, opacity: Math.min(1, fresh) }}>{t('Version 5.0.452 available')}</span>}
          </div>
        </div>
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 690, display: 'flex', justifyContent: 'center', gap: 26 }}>
        <Chip icon="arrow_download_24_regular" text={t('Downloads it')} show={pop(f, newAt + 16, { damping: 14 })} />
        <Chip icon="person_clock_24_regular" text={t('Restarts when you are away')} show={pop(f, restartAt - 4, { damping: 14 })} />
      </div>
      <div style={{ position: 'absolute', left: 960, top: 860, transform: 'translateX(-50%)' }}>
        <Osd icon="arrow_sync_24_filled" color="green" text={t('K-Desktop updated to version 5.0.452')} show={inOut(f, restartAt + 32, b.cue('settings') - 6, 8)} />
      </div>
      <Sfx at={b.from - 2} name="pop" volume={0.25} />
      <Sfx at={newAt - 4} name="blip" volume={0.3} />
      <Sfx at={restartAt - 6} name="rise" volume={0.2} />
      <Sfx at={restartAt + 30} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};

// The «When there is a new version» box of the real Settings › Updates page (capture pixels).
const MODE_BOX = { x: 1282, y: 263, w: 240, h: 38 };

/** Settings › Updates (the real page): «Tell me» instead of installing by itself. */
const SettingsPart: React.FC<{ b: BeatTimes; from: number }> = ({ b, from }) => {
  const f = useCurrentFrame();
  const tellAt = b.cue('tell');
  const open = inOut(f, tellAt - 14, tellAt + 60, 6);
  const options = [t("Don't check"), t('Tell me'), t('Install it by itself')];
  return (
    <CaptureShot name="settings-updates"
      shots={[{ at: from, x: 960, y: 540, s: 1.0 }, { at: from + 6, dur: 40, x: 1050, y: 300, s: 1.55 }]}
      focus={[{ from: from + 20, to: tellAt + 70, rect: [572, 254, 960, 56] }]}
      cursor={[{ at: from, x: 1500, y: 600 }, { at: tellAt - 30, x: MODE_BOX.x + 180, y: MODE_BOX.y + 20, dur: 14 }, { at: tellAt - 8, x: MODE_BOX.x + 90, y: MODE_BOX.y + 84, dur: 8 }]}
      clicks={[{ at: tellAt - 14 }, { at: tellAt }]}>
      <div style={{
        position: 'absolute', left: MODE_BOX.x, top: MODE_BOX.y + MODE_BOX.h + 4, width: MODE_BOX.w, padding: 4, borderRadius: 8, background: '#2b2b30',
        border: `1px solid ${C.line2}`, boxShadow: '0 14px 30px rgba(0,0,0,0.5)', opacity: open, transform: `translateY(${(1 - open) * -10}px)`, fontFamily: SANS,
      }}>
        {options.map((o, i) => (
          <div key={o} style={{
            position: 'relative', padding: '8px 12px', borderRadius: 6, fontSize: 14, color: C.text,
            background: i === 1 && f >= tellAt - 2 ? 'rgba(255,255,255,0.1)' : i === 2 && f < tellAt - 2 ? 'rgba(255,255,255,0.06)' : 'transparent',
          }}>
            {((i === 1 && f >= tellAt - 2) || (i === 2 && f < tellAt - 2)) && <div style={{ position: 'absolute', left: 0, top: 9, bottom: 9, width: 3, borderRadius: 2, background: C.accent }} />}
            {o}
          </div>
        ))}
      </div>
    </CaptureShot>
  );
};

/** Chapter «Download it»: the file, GitHub's Releases, a folder of your own, SmartScreen, updates. */
export const Download: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const get = B('get');
  const run = B('run');
  const smart = B('smart');
  const updates = B('updates');
  const settingsAt = updates.cue('settings') - 10;
  const releaseFrom = get.cue('github') - 10;
  return (
    <AbsoluteFill>
      <Part from={0} to={releaseFrom} first><ProgramPart get={get} /></Part>
      <Part from={releaseFrom} to={run.from - 4}><ReleasePart get={get} from={releaseFrom} to={run.from - 4} /></Part>
      <Part from={run.from - 4} to={smart.from - 4}><FolderPart run={run} /></Part>
      <Part from={smart.from - 4} to={updates.from - 4}><SmartPart smart={smart} /></Part>
      <Part from={updates.from - 4} to={settingsAt}><UpdatePart b={updates} /></Part>
      <Part from={settingsAt} to={durationInFrames} last><SettingsPart b={updates} from={settingsAt} /></Part>
    </AbsoluteFill>
  );
};
