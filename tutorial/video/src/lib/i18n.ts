import { getInputProps } from 'remotion';
import { ES } from './i18n.es';

/**
 * The texts the scenes draw, in the language of the video. They are written in English in the scenes, wrapped in
 * t('…'); each language has a table English → translation (i18n.es.ts), and whatever is missing stays in English
 * (node tools/i18n-check.mjs lists it). The language comes from the render's props as the bundle loads, so the
 * lists built when a module loads (the action families…) are translated too; Video sets it again on each render
 * (Remotion Studio switching compositions).
 */
let lang: string = (() => {
  try {
    return (getInputProps() as { lang?: string }).lang ?? 'en';
  } catch {
    return 'en';
  }
})();

const TABLES: Record<string, Record<string, string>> = { es: ES };

export const setLang = (value: string) => {
  lang = value;
};

export const t = (en: string): string => TABLES[lang]?.[en] ?? en;
