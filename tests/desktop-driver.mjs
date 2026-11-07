import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
export const root=fileURLToPath(new URL('../',import.meta.url));
export const executable=process.env.PET_CALENDAR_TEST_EXE||path.join(root,'dist/app/PetCalendar.exe');
export const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
export async function waitFor(fn,label,timeout=20000){const at=Date.now();let last;while(Date.now()-at<timeout){try{if(await fn())return;}catch(error){last=error;}await delay(80);}throw new Error('Timeout: '+label+(last?' '+last.message:''));}
export class DesktopDriver {
  constructor(dataDir,profileDir,appExecutable=executable){
    this.pending=new Map();this.next=0;this.diagnostic='';
    this.child=spawn(appExecutable,['--automation','--data-dir',dataDir,'--profile-dir',profileDir],{cwd:root,windowsHide:false,stdio:['pipe','pipe','pipe']});
    this.ready=new Promise((resolve,reject)=>{this.resolveReady=resolve;this.rejectReady=reject;});
    this.reader=createInterface({input:this.child.stdout});
    this.child.stderr.on('data',chunk=>{this.diagnostic+=chunk;});this.child.stdin.on('error',()=>{});
    this.reader.on('line',line=>{let message;try{message=JSON.parse(line.replace(/^\uFEFF/,''));}catch{return;}
      if(message.kind==='ready'){this.startupMs=message.elapsedMs;this.resolveReady(message);return;}
      if(message.kind==='fatal'){this.rejectReady(new Error(message.error));return;}
      const p=this.pending.get(message.id);if(!p)return;clearTimeout(p.timer);this.pending.delete(message.id);if(message.ok)p.resolve(message);else p.reject(new Error(message.error));
    });
    this.child.on('error',error=>this.rejectReady(error));
    this.child.on('exit',code=>{for(const p of this.pending.values()){clearTimeout(p.timer);p.reject(new Error(`Desktop exited ${code}: ${this.diagnostic}`));}this.pending.clear();this.rejectReady(new Error('Desktop exited '+code+': '+this.diagnostic));});
  }
  command(action,extra={}){const id=++this.next;return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('Command timeout '+action+' '+this.diagnostic));},15000);this.pending.set(id,{resolve,reject,timer});this.child.stdin.write(JSON.stringify({id,action,...extra})+'\n');});}
  async js(script){return JSON.parse((await this.command('evaluate',{script})).result);}
  async api(method,path,body,extra={}){return this.command('api',{method,path,...(body===undefined?{}:{body}),...extra});}
  async state(){return (await this.api('GET','/api/state')).body;}
  async save(state,extra={}){return this.api('PUT','/api/state',state,extra);}
  async close(){if(this.child.exitCode!==null)return;try{await this.command('exit');await waitFor(()=>this.child.exitCode!==null,'desktop exit',5000);}catch{this.child.kill();}this.reader.close();}
}
