import { spawn } from 'node:child_process';

const commands = [
  ['npm', ['run', 'build:config', '--', '--watch']],
  ['npm', ['run', 'build:overlay', '--', '--watch']]
];

const children = commands.map(([command, args]) => {
  const child = spawn(command, args, {
    stdio: 'inherit',
    shell: process.platform === 'win32'
  });

  child.on('exit', code => {
    if (code && code !== 0) {
      process.exitCode = code;
    }
  });

  return child;
});

process.on('SIGINT', () => {
  children.forEach(child => child.kill('SIGINT'));
});
