"""Local HTTPS browser acceptance test. Run against a Development instance only."""
import base64
import email
from email import policy
import io
import os
from pathlib import Path
import re
import sys
import uuid
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '.test-tools'))
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts' / 'qa'
OUT.mkdir(parents=True, exist_ok=True)
ORIGIN = os.environ.get('CLEARPARENT_TEST_ORIGIN', 'https://localhost:7273')
assert ORIGIN.startswith('https://localhost:'), 'Use only a disposable local development instance.'
PNG = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2S8AAAAASUVORK5CYII=')
with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    context = browser.new_context(ignore_https_errors=True, viewport={'width': 1440, 'height': 1000})
    page = context.new_page()
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.goto(ORIGIN)
    page.screenshot(path=str(OUT / 'home-desktop.png'), full_page=True)
    PNG = page.screenshot(clip={'x':140,'y':160,'width':500,'height':300})
    page.set_viewport_size({'width': 390, 'height': 844})
    page.screenshot(path=str(OUT / 'home-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
    page.set_viewport_size({'width': 1440, 'height': 1000})
    address = 'browser-' + uuid.uuid4().hex[:12] + '@example.test'
    page.goto(ORIGIN + '/account/register')
    page.get_by_label('Your name').fill('Browser QA Parent')
    page.get_by_label('Email', exact=True).fill(address)
    page.get_by_label('Password', exact=True).fill('Browser-Testing123!')
    page.get_by_label('Confirm password', exact=True).fill('Browser-Testing123!')
    page.get_by_role('button', name='Create account').click()
    expect(page.get_by_text('Check your email', exact=False)).to_be_visible()
    confirmation = None
    for path in sorted((ROOT / 'ClearParentLite' / 'App_Data' / 'mail').glob('*.eml'), key=lambda x: x.stat().st_mtime, reverse=True):
        message = email.message_from_bytes(path.read_bytes(), policy=policy.default)
        if address in str(message['To']):
            body = message.get_body(preferencelist=('plain',)) if message.is_multipart() else message
            confirmation = re.search(r'https://\S+', body.get_content()).group(0)
            break
    assert confirmation, 'Confirmation email was not written to local pickup directory.'
    page.goto(confirmation)
    page.goto(ORIGIN + '/account/login')
    page.get_by_label('Email', exact=True).fill(address)
    page.get_by_label('Password', exact=True).fill('Browser-Testing123!')
    page.get_by_role('button', name='Sign in', exact=True).click()
    page.wait_for_url('**/timeline')
    page.get_by_role('link', name='New entry', exact=False).click()
    dialog = page.get_by_role('dialog')
    expect(dialog).to_be_visible()
    dialog.get_by_label('Title', exact=True).fill('Browser acceptance event')
    dialog.get_by_label('Occurred on', exact=True).fill('2026-10-15')
    editor = dialog.locator('.ql-editor')
    editor.fill('A factual note from the browser test.')
    editor.evaluate("""(element, b64) => {
      const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
      const data = new DataTransfer();
      data.items.add(new File([bytes], 'pasted-screenshot.png', {type:'image/png'}));
      element.dispatchEvent(new ClipboardEvent('paste', {clipboardData:data, bubbles:true, cancelable:true}));
    }""", base64.b64encode(PNG).decode())
    expect(editor.locator('img')).to_have_count(1)
    docx = io.BytesIO()
    with zipfile.ZipFile(docx, 'w') as z:
        z.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        z.writestr('word/document.xml', '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>QA document</w:t></w:r></w:p></w:body></w:document>')
    dialog.locator('[data-entry-files]').set_input_files([{'name':'notes.docx','mimeType':'application/vnd.openxmlformats-officedocument.wordprocessingml.document','buffer':docx.getvalue()},{'name':'record.pdf','mimeType':'application/pdf','buffer':b'%PDF-1.4\nQA attachment\n%%EOF'}])
    dialog.get_by_role('button', name='Save entry', exact=True).click()
    try:
        page.wait_for_url(re.compile(r'.*/entries/[a-f0-9-]{36}$'), timeout=30000)
    except Exception:
        page.screenshot(path=str(OUT / 'browser-failure.png'), full_page=True)
        print('Save status:', dialog.locator('[data-file-status]').inner_text(), 'JS errors:', errors)
        raise
    entry_url = page.url
    expect(page.get_by_role('heading', name='Browser acceptance event')).to_be_visible()
    expect(page.locator('article.prose img')).to_have_count(1)
    assert page.locator('article.prose img').evaluate('(img)=>img.complete && img.naturalWidth > 0')
    expect(page.locator('.file-list li')).to_have_count(3)
    page.screenshot(path=str(OUT / 'entry-desktop.png'), full_page=True)
    page.get_by_role('link', name='Edit entry', exact=True).click()
    page.get_by_label('Title', exact=True).fill('Browser event revised')
    page.get_by_role('button', name='Save entry', exact=True).click()
    page.wait_for_url(entry_url)
    page.locator('.ql-editor').fill('A timestamped follow-up.')
    page.get_by_role('button', name='Add update', exact=True).click()
    expect(page.get_by_text('A timestamped follow-up.', exact=True)).to_be_visible()
    page.reload()
    expect(page.locator('article.prose img')).to_have_count(1)
    page.goto(ORIGIN + '/timeline')
    page.locator('[name=q]').fill('timestamped follow-up')
    page.get_by_role('button', name='Filter', exact=True).click()
    expect(page.get_by_role('link', name='Browser event revised', exact=False)).to_be_visible()
    page.set_viewport_size({'width':390,'height':844})
    page.screenshot(path=str(OUT / 'timeline-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
    page.set_viewport_size({'width':1440,'height':1000})
    page.goto(ORIGIN + '/shares')
    page.locator('[name=Scope]').select_option('SelectedEntries')
    page.locator('[name=EntryIds]').check()
    page.locator('[name=AccessMethod]').select_option('Link')
    page.get_by_role('button', name='Create read-only share').click()
    link = page.locator('#share-url').input_value()
    guest = browser.new_context(ignore_https_errors=True)
    shared = guest.new_page()
    shared.goto(link)
    shared.get_by_role('link', name='Browser event revised', exact=False).click()
    expect(shared.get_by_role('heading', name='Browser event revised')).to_be_visible()
    expect(shared.get_by_role('link', name='Edit entry', exact=True)).to_have_count(0)
    page.goto(ORIGIN + '/exports')
    page.locator('[name=scope]').select_option('selected')
    page.locator('[name=entryIds]').check()
    page.locator('[name=format]').select_option('zip')
    with page.expect_download() as download_info:
        page.get_by_role('button', name='Download export').click()
    download = download_info.value
    download.save_as(str(OUT / 'browser-export.zip'))
    with zipfile.ZipFile(OUT / 'browser-export.zip') as z:
        assert len([x for x in z.namelist() if x.startswith('attachments/')]) == 3
        (OUT / 'browser-report.pdf').write_bytes(z.read('report.pdf'))
    page.goto(ORIGIN + '/shares')
    page.get_by_role('button', name='Revoke access').click()
    assert guest.request.get(entry_url).status == 404
    assert not errors, errors
    print('Browser acceptance passed: registration, confirmation, login, modal, pasted image, DOCX/PDF upload, edit, update, search, mobile layout, selected share, revocation and ZIP download.')
    browser.close()
