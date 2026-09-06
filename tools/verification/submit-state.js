// UX-04: a submitted form must show it is working and must not accept a second
// press. There is no spinner anywhere in the app - every user-initiated action
// is a full navigation or a form post - so this is the loading state.
const fs=require('fs'), puppeteer=require('puppeteer-core');
const chrome = require('./config').chromePath();
const config = require('./config');
const BASE = config.baseUrl;
const fx=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
const results=[];
const check=(n,p,d)=>{results.push(p);console.log(`  ${p?'PASS':'FAIL'}  ${n}${d?'\n          '+d:''}`);};
(async()=>{
 const b=await puppeteer.launch({executablePath:chrome,headless:'new',
  args:['--ignore-certificate-errors','--no-sandbox'],defaultViewport:{width:1440,height:900}});
 const ctx=await b.createBrowserContext(); const page=await ctx.newPage();
 await page.goto(BASE+'/Account/Login',{waitUntil:'networkidle2'});
 await page.type('#loginUsername',fx.teacher.username);
 await page.type('#loginPassword',fx.teacher.password);
 await Promise.all([page.waitForNavigation({waitUntil:'networkidle2'}),page.click('button[type="submit"]')]);
 await page.goto(BASE+'/Teacher/Restrictions',{waitUntil:'networkidle2'});

 // Submit without navigating, so the in-flight state can be observed.
 const state=await page.evaluate(()=>{
   const form=document.querySelector('#ruleModal form');
   if(!form) return {error:'no form found'};
   // Registered on the document, after the guard, so the guard still runs. A
   // listener on the form itself fires first and its preventDefault makes the
   // guard skip the submission - which is correct behaviour, not a defect.
   document.addEventListener('submit',e=>e.preventDefault());
   const button=form.querySelector('button[type=submit], button:not([type])');
   form.querySelector('[name=Target]').value='loading-state-probe.example';
   button.click();
   return new Promise(resolve=>setTimeout(()=>resolve({
     busy: form.getAttribute('aria-busy'),
     buttonDisabled: button.disabled,
     buttonText: button.textContent.trim()
   }),120));
 });

 if(state.error){ check('the restriction form was found',false,state.error); }
 else {
   check('the form marks itself busy while the submission is in flight',
     state.busy==='true','aria-busy='+state.busy);
   check('the submit button is disabled so it cannot be pressed twice',
     state.buttonDisabled===true,'disabled='+state.buttonDisabled);
   console.log('          button reads: "'+state.buttonText+'"');
 }

 // And it must recover rather than staying stuck.
 const released=await page.evaluate(()=>new Promise(resolve=>{
   const form=document.querySelector('#ruleModal form');
   const button=form.querySelector('button[type=submit], button:not([type])');
   const started=Date.now();
   const tick=()=>{
     if(!button.disabled) return resolve({released:true,after:Date.now()-started});
     if(Date.now()-started>25000) return resolve({released:false,after:Date.now()-started});
     setTimeout(tick,500);
   };
   tick();
 }));
 check('a submission that never completes releases rather than locking the form',
   released.released, released.released?`released after ${Math.round(released.after/1000)}s`:'still disabled after 25s');

 await b.close();
 const failed=results.filter(r=>!r).length;
 console.log(`\n${results.length-failed}/${results.length} checks passed`);
 process.exit(failed?1:0);
})().catch(e=>{console.error('ERROR '+e.message);process.exit(2);});
