import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { NetworkScene } from 'ubiq-server/ubiq';
import { ServiceController } from '../../components/service';

function getPythonCommand(appRoot: string): string {
  const venvRoot = path.join(appRoot, 'venv');
  const candidate = process.platform === 'win32'
    ? path.join(venvRoot, 'Scripts', 'python.exe')
    : path.join(venvRoot, 'bin', 'python');

  if (fs.existsSync(candidate)) {
    return candidate;
  }

  console.warn('[CodeGenerationService] Python venv not found. Create it from README. Falling back to python on PATH.');
  return process.platform === 'win32' ? 'python' : 'python3';
}

export class CodeGenerationService extends ServiceController {
  private pythonCommand: string;
  private pythonOptions: string[];

  constructor(scene: NetworkScene, config: any = {}) {
    super(scene, 'CodeGenerationService');

    const serviceDir = path.dirname(fileURLToPath(import.meta.url));
    const appRoot = path.resolve(serviceDir, '..', '..');
    const apiKey = process.env.OPENAI_API_KEY || config?.credentials?.openAI?.key || config?.key || '';
    const model = process.env.OPENAI_MODEL || config?.credentials?.openAI?.model || config?.openAIModel || 'gpt-4o-mini';

    this.pythonCommand = getPythonCommand(appRoot);
    this.pythonOptions = [
      '-u',
      path.join(serviceDir, 'openai_chatgpt_api.py'),
      '--preprompt',
      config?.preprompt || '',
      '--prompt_suffix',
      config?.prompt_suffix || '',
      '--key',
      apiKey,
      '--model',
      model,
    ];

    this.ensureDefaultChildProcess();
  }

  private ensureDefaultChildProcess(): void {
    if (!this.childProcesses.default) {
      this.registerChildProcess('default', this.pythonCommand, this.pythonOptions);
    }
  }

  sendToChildProcess(identifier: string, data: string | Buffer): boolean {
    if (identifier === 'default') {
      this.ensureDefaultChildProcess();
    }
    return super.sendToChildProcess(identifier, data);
  }
}
