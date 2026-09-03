import loader from '@monaco-editor/loader';
import * as monaco from 'monaco-editor';
import EditorWorker from 'monaco-editor/esm/vs/editor/editor.worker?worker';
import CssWorker from 'monaco-editor/esm/vs/language/css/css.worker?worker';
import HtmlWorker from 'monaco-editor/esm/vs/language/html/html.worker?worker';
import JsonWorker from 'monaco-editor/esm/vs/language/json/json.worker?worker';
import TsWorker from 'monaco-editor/esm/vs/language/typescript/ts.worker?worker';
import YamlWorker from '@/workers/yaml.worker?worker';

type MonacoWorkerFactory = new () => Worker;

interface MonacoEnvironmentConfig {
  getWorker: (_workerId: string, label: string) => Worker;
}

function createWorker(WorkerFactory: MonacoWorkerFactory) {
  return new WorkerFactory();
}

export function initializeMonaco() {
  globalThis.MonacoEnvironment = {
    getWorker(_workerId: string, label: string) {
      if (label === 'json') {
        return createWorker(JsonWorker);
      }

      if (label === 'css' || label === 'less' || label === 'scss') {
        return createWorker(CssWorker);
      }

      if (label === 'html' || label === 'handlebars' || label === 'razor') {
        return createWorker(HtmlWorker);
      }

      if (label === 'javascript' || label === 'typescript') {
        return createWorker(TsWorker);
      }

      if (label === 'yaml') {
        return createWorker(YamlWorker);
      }

      return createWorker(EditorWorker);
    },
  } satisfies MonacoEnvironmentConfig;

  loader.config({ monaco });
}
