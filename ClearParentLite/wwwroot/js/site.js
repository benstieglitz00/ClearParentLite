'use strict';
function setupEditors(root=document){
  root.querySelectorAll('[data-entry-form]').forEach(form=>{
    if(form.dataset.ready)return;form.dataset.ready='true';
    const source=form.querySelector('[data-editor]');if(!source)return;
    const pending=[];const status=form.querySelector('[data-file-status]')||document.createElement('div');
    if(!status.parentNode){status.setAttribute('role','status');form.append(status);}
    let quill;
    if(window.Quill){
      const host=document.createElement('div');source.after(host);source.classList.add('hidden');source.removeAttribute('required');
      quill=new Quill(host,{theme:'snow',modules:{toolbar:[['bold','italic','underline'],[{header:[2,3,false]}],[{list:'ordered'},{list:'bullet'}],['blockquote','clean']]},formats:['bold','italic','underline','header','list','blockquote','image']});
      quill.clipboard.dangerouslyPasteHTML(source.value||'');
      quill.root.setAttribute('aria-label',source.name==='bodyHtml'?'Update text':'Entry description');
      const add=file=>{pending.push({file,index:null,url:null});status.textContent=pending.length+' file(s) ready to upload.';};
      quill.root.addEventListener('paste',e=>{const images=[...e.clipboardData.items].filter(x=>x.kind==='file'&&x.type.startsWith('image/'));if(!images.length)return;e.preventDefault();e.stopImmediatePropagation();images.forEach(item=>{const file=item.getAsFile();if(!file)return;const url=URL.createObjectURL(file);const range=quill.getSelection(true);quill.insertEmbed(range.index,'image',url,'user');quill.setSelection(range.index+1);pending.push({file,url});status.textContent=pending.length+' screenshot(s) ready to upload.';});},true);
      form.addEventListener('dragover',e=>{if(e.dataTransfer.types.includes('Files'))e.preventDefault();});
      form.addEventListener('drop',e=>{if(e.dataTransfer.files.length){e.preventDefault();[...e.dataTransfer.files].forEach(add);}});
      const fileInput=form.querySelector('[data-entry-files]');if(fileInput)fileInput.addEventListener('change',()=>{[...fileInput.files].forEach(add);fileInput.value='';});
    }
    form.addEventListener('submit',async e=>{
      if(!quill)return;
      e.preventDefault();if(form.dataset.saving)return;
      source.value=quill.root.innerHTML;
      if(!quill.getText().trim()&&!pending.length&&!quill.root.querySelector('img')){status.textContent='Add a description before saving.';return;}
      const button=form.querySelector('button[type=submit],button.primary');if(button)button.disabled=true;form.dataset.saving='true';
      let id=form.dataset.entryId;
      const token=form.querySelector('[name=__RequestVerificationToken]').value;
      try{
        if(!id){
          const data=new FormData(form);
          if(pending.some(x=>x.url))data.set('DescriptionHtml',source.value+'<p>[Screenshot attached]</p>');
          const created=await fetch(form.action,{method:'POST',body:data,headers:{Accept:'application/json'}});
          if(!created.ok||!created.headers.get('content-type')?.includes('application/json')){const text=await created.text();const parsed=new DOMParser().parseFromString(text,'text/html');throw new Error(parsed.querySelector('.validation')?.textContent.trim()||'Unable to save. Check all required fields.');}
          const result=await created.json();id=result.id;form.querySelector('[name=RowVersion]').value=result.rowVersion;form.dataset.entryId=id;form.action='/entries/'+id+'/edit';
          status.textContent='Entry saved. Uploading attachments…';
        }
        for(let i=0;i<pending.length;i++){
          const item=pending[i];if(item.uploaded)continue;
          const result=await new Promise((resolve,reject)=>{
            const xhr=new XMLHttpRequest();xhr.open('POST','/entries/'+id+'/attachments');xhr.setRequestHeader('Accept','application/json');xhr.setRequestHeader('X-CSRF-TOKEN',token);
            xhr.upload.onprogress=event=>{if(event.lengthComputable)status.textContent='Uploading '+item.file.name+' · '+Math.round(event.loaded/event.total*100)+'%';};
            xhr.onload=()=>{if(xhr.status>=200&&xhr.status<300){try{resolve(JSON.parse(xhr.responseText));}catch{reject(new Error('Upload failed. Please sign in again.'));}}else reject(new Error('Upload failed: '+xhr.responseText.slice(0,250)));};xhr.onerror=()=>reject(new Error('Connection lost. Retry to upload remaining files.'));
            const data=new FormData();data.append('file',item.file,item.file.name);xhr.send(data);
          });
          item.uploaded=true;if(item.url){quill.root.querySelectorAll('img').forEach(img=>{if(img.getAttribute('src')===item.url)img.setAttribute('src',result.url);});URL.revokeObjectURL(item.url);}
        }
        source.value=quill.root.innerHTML;
        if(form.action.includes('/updates')){
          const response=await fetch(form.action,{method:'POST',body:new FormData(form)});if(!response.ok)throw new Error('Unable to save this update.');window.location.href='/entries/'+id;return;
        }
        const response=await fetch('/entries/'+id+'/edit',{method:'POST',body:new FormData(form)});
        if(!response.ok||response.url.endsWith('/edit')){const page=new DOMParser().parseFromString(await response.text(),'text/html');throw new Error(page.querySelector('.validation')?.textContent.trim()||'Unable to save changes. Your draft is preserved.');}
        window.location.href='/entries/'+id;
      }catch(error){status.textContent=error.message+(id?' Your entry exists; this draft stays here so you can retry.':'');status.classList.add('validation');}
      finally{delete form.dataset.saving;if(button)button.disabled=false;}
    });
  });
}
setupEditors();
document.querySelectorAll('form[data-confirm]').forEach(form=>form.addEventListener('submit',e=>{if(!confirm(form.dataset.confirm))e.preventDefault();}));
document.querySelectorAll('.sidebar>a').forEach(a=>{if(location.pathname.startsWith(a.getAttribute('href')))a.classList.add('active');});
const dialog=document.querySelector('#entry-dialog');
async function newEntry(event){
  if(!dialog?.showModal)return;if(event)event.preventDefault();
  const response=await fetch('/entries/new');if(!response.ok){location.href='/entries/new';return;}
  const page=new DOMParser().parseFromString(await response.text(),'text/html');const node=page.querySelector('.narrow');
  if(!node){location.href=response.url;return;}dialog.querySelector('[data-dialog-content]').replaceChildren(node);node.querySelector('form').action='/entries/new';setupEditors(dialog);dialog.showModal();dialog.querySelector('[name=Title]').focus();
}
document.querySelectorAll('[data-new-entry]').forEach(a=>a.addEventListener('click',newEntry));
dialog?.querySelector('.dialog-close').addEventListener('click',()=>dialog.close());
document.addEventListener('keydown',e=>{if(e.key==='n'&&!e.ctrlKey&&!e.metaKey&&!e.altKey&&!e.target.closest('input,textarea,[contenteditable=true]')&&document.querySelector('[data-new-entry]'))newEntry(e);});
