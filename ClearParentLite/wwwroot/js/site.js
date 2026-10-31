"use strict";
let leavingAfterSave=false;
async function savedJson(response){
  if(response.ok && response.headers.get('content-type')?.includes('application/json'))return response.json();
  if(response.redirected || [401,403].includes(response.status))throw new Error('Your session may have expired. Sign in in another tab, then retry here. Your draft is preserved.');
  const text=await response.text();
  const page=new DOMParser().parseFromString(text,'text/html');
  throw new Error(page.querySelector('.validation')?.textContent.trim() || (response.headers.get('content-type')?.includes('text/plain')?text.slice(0,350):'Unable to save. Check the fields and retry. Your draft is preserved.'));
}
function setupEditors(root=document){
  root.querySelectorAll('[data-entry-form]').forEach(form=>{
    if(form.dataset.ready)return;form.dataset.ready='true';
    const source=form.querySelector('[data-editor]');if(!source||!window.Quill)return;
    const pending=[],readers=[];
    const status=form.querySelector('[data-file-status]')||document.createElement('div');
    if(!status.parentNode){status.setAttribute('role','status');status.setAttribute('aria-live','polite');form.append(status);}
    const host=document.createElement('div');source.after(host);source.classList.add('hidden');source.removeAttribute('required');
    const quill=new Quill(host,{theme:'snow',modules:{toolbar:[['bold','italic','underline'],[{header:[2,3,false]}],[{list:'ordered'},{list:'bullet'}],['blockquote','clean']]},formats:['bold','italic','underline','header','list','blockquote','image']});
    quill.clipboard.dangerouslyPasteHTML(source.value||'');
    quill.root.setAttribute('aria-label',source.name==='bodyHtml'?'Update text':'Entry description');
    quill.root.setAttribute('role','textbox');quill.root.setAttribute('aria-multiline','true');
    form.querySelectorAll('.ql-toolbar button').forEach(button=>{const name=[...button.classList].find(c=>c.startsWith('ql-'))?.slice(3)||'format';button.setAttribute('aria-label',button.value?name+' '+button.value:name);});
    form.querySelectorAll('.ql-picker-label').forEach(label=>label.setAttribute('aria-label','Text heading style'));
    const dirty=()=>{form.dataset.dirty='true';};
    form.addEventListener('input',dirty);form.addEventListener('change',dirty);quill.on('text-change',(_,__,origin)=>{if(origin==='user')dirty();});
    const queue=file=>{
      if(!/\.(png|jpe?g|gif|webp|pdf|docx?)$/i.test(file.name)||file.size===0||file.size>20*1024*1024){status.textContent='Choose PNG, JPEG, GIF, WebP, PDF, DOC or DOCX files up to 20 MB each.';status.classList.add('validation');return null;}
      const item={file,requestId:crypto.randomUUID(),url:null,uploaded:false};pending.push(item);dirty();status.classList.remove('validation');status.textContent=pending.length+' file(s) ready to upload.';return item;
    };
    quill.root.addEventListener('paste',e=>{
      const images=[...e.clipboardData.items].filter(x=>x.kind==='file'&&x.type.startsWith('image/'));if(!images.length)return;
      e.preventDefault();e.stopImmediatePropagation();
      images.forEach(clip=>{const file=clip.getAsFile();if(!file)return;const item=queue(file);if(!item)return;
        readers.push(new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>{item.url=reader.result;const range=quill.getSelection(true);quill.insertEmbed(range.index,'image',item.url,'user');quill.setSelection(range.index+1);resolve();};reader.onerror=()=>reject(new Error('Unable to read the pasted screenshot. Paste it again.'));reader.readAsDataURL(file);}));
      });
    },true);
    form.addEventListener('dragover',e=>{if(e.dataTransfer.types.includes('Files'))e.preventDefault();});
    form.addEventListener('drop',e=>{if(e.dataTransfer.files.length){e.preventDefault();[...e.dataTransfer.files].forEach(queue);}});
    const fileInput=form.querySelector('[data-entry-files]');if(fileInput)fileInput.addEventListener('change',()=>{[...fileInput.files].forEach(queue);fileInput.value='';});
    form.addEventListener('submit',async e=>{
      e.preventDefault();if(form.dataset.saving)return;
      form.dataset.saving='true';const button=form.querySelector('button[type=submit],button.primary');if(button)button.disabled=true;
      let id=form.dataset.entryId;const token=form.querySelector('[name=__RequestVerificationToken]').value;let createdHere=false;
      try{
        await Promise.all(readers);source.value=quill.root.innerHTML;
        if(!quill.getText().trim()&&!quill.root.querySelector('img'))throw new Error('Add a description before saving.');
        if(!id){
          const data=new FormData(form),draft=quill.root.cloneNode(true);
          draft.querySelectorAll('img').forEach(img=>{if(pending.some(x=>x.url===img.getAttribute('src')))img.replaceWith(document.createTextNode('[Screenshot attached]'));});data.set('DescriptionHtml',draft.innerHTML);
          const result=await savedJson(await fetch(form.action,{method:'POST',body:data,headers:{Accept:'application/json'}}));
          id=result.id;form.querySelector('[name=RowVersion]').value=result.rowVersion;form.dataset.entryId=id;form.action='/entries/'+id+'/edit';createdHere=true;
        }
        let insertedImage=false;
        for(const item of pending){
          if(item.uploaded)continue;
          if(item.url&&![...quill.root.querySelectorAll('img')].some(img=>img.getAttribute('src')===item.url))continue;
          const result=await new Promise((resolve,reject)=>{
            const xhr=new XMLHttpRequest();xhr.open('POST','/entries/'+id+'/attachments');xhr.setRequestHeader('Accept','application/json');xhr.setRequestHeader('X-CSRF-TOKEN',token);
            xhr.upload.onprogress=event=>{if(event.lengthComputable)status.textContent='Uploading '+item.file.name+' - '+Math.round(event.loaded/event.total*100)+'%';};
            xhr.onload=()=>{if(xhr.status>=200&&xhr.status<300&&xhr.getResponseHeader('content-type')?.includes('application/json')){try{resolve(JSON.parse(xhr.responseText));}catch{reject(new Error('Upload response was incomplete. Retry to confirm the saved file.'));}}else reject(new Error(xhr.responseURL.includes('/account/')?'Sign in in another tab, then retry. Your draft is preserved.':'Upload failed: '+(xhr.getResponseHeader('content-type')?.includes('text/plain')?xhr.responseText.slice(0,250):'Please retry.')));};xhr.onerror=()=>reject(new Error('Connection lost. Retry to upload remaining files.'));
            const data=new FormData();data.append('file',item.file,item.file.name);data.append('requestId',item.requestId);xhr.send(data);
          });
          item.uploaded=true;
          if(item.url){quill.root.querySelectorAll('img').forEach(img=>{if(img.getAttribute('src')===item.url)img.setAttribute('src',result.url);});insertedImage=true;}
        }
        quill.update('silent');source.value=quill.root.innerHTML;
        const isUpdate=form.action.includes('/updates');
        if(!createdHere||insertedImage){
          await savedJson(await fetch(isUpdate?form.action:'/entries/'+id+'/edit',{method:'POST',body:new FormData(form),headers:{Accept:'application/json'}}));
        }
        form.dataset.dirty='false';leavingAfterSave=true;window.location.href='/entries/'+id;
      }catch(error){status.textContent=error.message+(id?' The entry is saved; your current draft stays here for retry.':'');status.classList.add('validation');}
      finally{delete form.dataset.saving;if(button)button.disabled=false;}
    });
  });
}
setupEditors();
document.querySelectorAll('[data-scope-options]').forEach(form=>{
  const scope=form.querySelector('[name=scope],[name=Scope]'),method=form.querySelector('[name=AccessMethod]');
  const show=(selector,active)=>form.querySelectorAll(selector).forEach(section=>{section.classList.toggle('hidden',!active);section.querySelectorAll('input').forEach(input=>{input.disabled=!active;});});
  const refresh=()=>{
    show('[data-selected-records]',scope.value==='selected'||scope.value==='SelectedEntries');
    show('[data-date-range]',scope.value==='dates');
    if(method){show('[data-invitation-email]',method.value==='Invitation');show('[data-link-warning]',method.value==='Link');const address=form.querySelector('[name=Email]');if(address)address.required=method.value==='Invitation';}
  };
  scope.addEventListener('change',refresh);method?.addEventListener('change',refresh);refresh();
});
window.addEventListener('beforeunload',e=>{if(!leavingAfterSave&&document.querySelector('[data-entry-form][data-dirty=true]')){e.preventDefault();e.returnValue='';}});
document.querySelectorAll('form[data-confirm]').forEach(form=>form.addEventListener('submit',e=>{if(!confirm(form.dataset.confirm))e.preventDefault();}));
document.querySelectorAll('.sidebar>a').forEach(a=>{if(location.pathname.startsWith(a.getAttribute('href')))a.classList.add('active');});
const dialog=document.querySelector('#entry-dialog');
function canCloseDraft(){return !dialog.querySelector('[data-dirty=true]')||confirm('Discard this unsaved draft?');}
async function newEntry(event){
  if(!dialog?.showModal)return;if(event)event.preventDefault();
  if(dialog.open)return;
  const response=await fetch('/entries/new');if(!response.ok){location.href='/entries/new';return;}
  const page=new DOMParser().parseFromString(await response.text(),'text/html');const node=page.querySelector('.narrow');
  if(!node){location.href=response.url;return;}dialog.querySelector('[data-dialog-content]').replaceChildren(node);node.querySelector('form').action='/entries/new';setupEditors(dialog);dialog.showModal();dialog.querySelector('[name=Title]').focus();
}
document.querySelectorAll('[data-new-entry]').forEach(a=>a.addEventListener('click',newEntry));
dialog?.querySelector('.dialog-close').addEventListener('click',()=>{if(canCloseDraft()){dialog.querySelector('[data-entry-form]')?.setAttribute('data-dirty','false');dialog.close();}});
dialog?.addEventListener('cancel',e=>{if(!canCloseDraft())e.preventDefault();else dialog.querySelector('[data-entry-form]')?.setAttribute('data-dirty','false');});
document.addEventListener('keydown',e=>{if(e.key==='n'&&!e.ctrlKey&&!e.metaKey&&!e.altKey&&!e.target.closest('input,textarea,[contenteditable=true]')&&document.querySelector('[data-new-entry]'))newEntry(e);});
