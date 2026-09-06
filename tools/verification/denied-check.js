// UX-04: what an authenticated user sees when their role does not reach a page.
// It used to be the sign-in form, telling someone already signed in to sign in.
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

 const res=await page.goto(BASE+'/Admin/AuditLogs',{waitUntil:'networkidle2'});
 const landed=page.url().replace(BASE,'');
 console.log(`  a teacher opened /Admin/AuditLogs -> HTTP ${res.status()}, landed on ${landed}\n`);

 const s=await page.evaluate(()=>({
   heading:(document.querySelector('h1')||{}).textContent?.trim(),
   body:document.body.textContent||'',
   hasLoginForm:!!document.getElementById('loginUsername'),
   hasSidebar:!!document.querySelector('.sidebar-nav'),
   wayBack:[...document.querySelectorAll('a.btn')].map(a=>a.textContent.trim()),
   title:document.title
 }));
 check('it is no longer the sign-in form',!s.hasLoginForm);
 check('it says access was denied',/do not have access/i.test(s.heading||''),s.heading);
 check('it names who you are signed in as',/signed in as/i.test(s.body)&&s.body.includes('Tessa'),
   (s.body.match(/You are signed in as[^.]*\./)||[''])[0].trim());
 check('it says signing in again will not help',/signing in again will not open it/i.test(s.body));
 check('it offers a way back to your own portal',s.wayBack.some(t=>/go to your dashboard/i.test(t)),s.wayBack.join(' | '));
 check('it keeps your own navigation',s.hasSidebar);
 check('the response is 403, not 200',res.status()===403,'HTTP '+res.status());

 // An anonymous visitor still belongs on the sign-in form.
 const anonCtx=await b.createBrowserContext(); const anon=await anonCtx.newPage();
 await anon.goto(BASE+'/Account/AccessDenied',{waitUntil:'networkidle2'});
 const anonHasLogin=await anon.evaluate(()=>!!document.getElementById('loginUsername'));
 check('an anonymous visitor is sent to sign in instead',anonHasLogin,anon.url().replace(BASE,''));

 await b.close();
 const failed=results.filter(r=>!r).length;
 console.log(`\n${results.length-failed}/${results.length} checks passed`);
 process.exit(failed?1:0);
})().catch(e=>{console.error('ERROR '+e.message);process.exit(2);});
