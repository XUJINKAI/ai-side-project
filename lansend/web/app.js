const $ = (selector, root = document) => root.querySelector(selector);

function fmt(bytes) {
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = Number(bytes) || 0;
  let index = 0;
  while (value >= 1024 && index < units.length - 1) { value /= 1024; index++; }
  return `${value.toFixed(index === 0 || value >= 10 ? 0 : 1)} ${units[index]}`;
}

function login() {
  const next = `${location.pathname}${location.search}${location.hash}`;
  location.assign(`/login.html?next=${encodeURIComponent(next)}`);
}

async function api(path, opts = {}) {
  const response = await fetch(`/api/v1/${path}`, opts);
  if (response.status === 401) { login(); throw Error('需要访问令牌'); }
  if (!response.ok) {
    let payload = {};
    try { payload = await response.json(); } catch {}
    throw Error(payload.error?.message || `请求失败 (${response.status})`);
  }
  return response.status === 204 ? null : response.json();
}

function renderBreadcrumb(items) {
  const path = $('#path');
  path.replaceChildren();
  items.forEach((item, index) => {
    if (index) {
      const separator = document.createElement('span');
      separator.className = 'breadcrumb-separator';
      separator.textContent = '›';
      separator.setAttribute('aria-hidden', 'true');
      path.append(separator);
    }
    const link = document.createElement('button');
    link.type = 'button';
    link.textContent = item.name;
    link.title = `转到 ${item.name}`;
    link.onclick = () => files(item.id);
    if (index === items.length - 1) {
      link.classList.add('current');
      link.setAttribute('aria-current', 'page');
    }
    path.append(link);
  });
}

function decodeText(buffer) {
  const bytes = new Uint8Array(buffer);
  const candidates = [];
  if (bytes[0] === 0xff && bytes[1] === 0xfe) candidates.push('utf-16le');
  else if (bytes[0] === 0xfe && bytes[1] === 0xff) candidates.push('utf-16be');
  else {
    let evenNUL = 0;
    let oddNUL = 0;
    for (let index = 0; index < Math.min(bytes.length, 8192); index++) {
      if (bytes[index] === 0) index % 2 ? oddNUL++ : evenNUL++;
    }
    if (oddNUL > evenNUL * 4) candidates.push('utf-16le');
    else if (evenNUL > oddNUL * 4) candidates.push('utf-16be');
  }
  candidates.push('utf-8', 'gb18030');
  for (const encoding of candidates) {
    try { return new TextDecoder(encoding, { fatal: true }).decode(buffer); } catch {}
  }
  return new TextDecoder('utf-8').decode(buffer);
}

async function preview(file) {
  const dialog = $('#preview-dialog');
  const body = $('#preview-body');
  const previewURL = `/api/v1/files/${file.id}?preview=${file.preview}`;
  $('#preview-title').textContent = file.name;
  $('#preview-download').href = `/api/v1/files/${file.id}`;
  body.replaceChildren();
  dialog.showModal();
  if (file.preview === 'image') {
    const image = document.createElement('img');
    image.alt = file.name;
    image.src = previewURL;
    body.append(image);
    return;
  }
  const loading = document.createElement('p');
  loading.textContent = '正在加载预览…';
  body.append(loading);
  try {
    const response = await fetch(previewURL);
    if (response.status === 401) return login();
    if (!response.ok) throw Error(`请求失败 (${response.status})`);
    const buffer = await response.arrayBuffer();
    const content = decodeText(buffer);
    const pre = document.createElement('pre');
    pre.textContent = content;
    if (response.headers.get('X-LanSend-Preview-Truncated') === 'true') {
      const notice = document.createElement('p');
      notice.className = 'preview-notice';
      notice.textContent = `文件较大，仅显示开头 ${fmt(buffer.byteLength)}；请下载文件查看完整内容。`;
      body.replaceChildren(notice, pre);
    } else {
      body.replaceChildren(pre);
    }
  } catch (error) {
    loading.textContent = `无法预览：${error.message}`;
  }
}

async function files(id = '') {
  const list = $('#files');
  try {
    const payload = await api(`files${id ? `?path=${encodeURIComponent(id)}` : ''}`);
    renderBreadcrumb(payload.breadcrumbs || [{ name: '下载目录', id: '' }]);
    $('#up').disabled = !id;
    list.replaceChildren();
    if (!Array.isArray(payload.files)) throw Error('文件列表响应格式错误');
    if (!payload.files.length) {
      const empty = document.createElement('li');
      empty.textContent = '此目录中没有可下载的文件';
      list.append(empty);
      return;
    }
    for (const file of payload.files) {
      const row = $('#file-row').content.firstElementChild.cloneNode(true);
      const link = $('.name', row);
      const kind = file.directory ? 'directory' : file.preview || 'download';
      link.textContent = file.directory ? `📁 ${file.name}` : file.name;
      link.href = file.directory || kind !== 'download' ? '#' : `/api/v1/files/${file.id}`;
      if (file.directory) link.onclick = event => { event.preventDefault(); files(file.id); };
      else if (kind !== 'download') link.onclick = event => { event.preventDefault(); preview(file); };
      const hint = kind === 'text' ? ' · 文本 · 点击预览' : kind === 'image' ? ' · 图片 · 点击预览' : '';
      link.nextElementSibling.textContent = file.directory ? '目录' : `${fmt(file.size)} · ${new Date(file.modified).toLocaleString()}${hint}`;
      list.append(row);
    }
  } catch (error) {
    list.replaceChildren();
    const row = document.createElement('li');
    row.textContent = `无法加载文件：${error.message}`;
    list.append(row);
    $('#status').textContent = error.message;
  }
}

function selectTab(name) {
  for (const tabName of ['download', 'upload']) {
    const selected = tabName === name;
    const tab = $(`#${tabName}-tab`);
    tab.setAttribute('aria-selected', String(selected));
    tab.tabIndex = selected ? 0 : -1;
    $(`#${tabName}-panel`).hidden = !selected;
  }
}

function upload(file) {
  const row = document.createElement('div');
  row.textContent = `上传 ${file.name}…`;
  $('#uploads').append(row);
  const body = new FormData();
  body.append('file', file);
  const xhr = new XMLHttpRequest();
  xhr.open('POST', '/api/v1/upload');
  xhr.upload.onprogress = event => {
    if (event.lengthComputable) row.textContent = `上传 ${file.name}：${Math.round(event.loaded / event.total * 100)}%`;
  };
  xhr.onload = () => {
    if (xhr.status === 401) return login();
    row.textContent = xhr.status < 300 ? `已上传：${file.name}` : `上传失败：${file.name}`;
  };
  xhr.onerror = () => { row.textContent = `上传失败：${file.name}`; };
  xhr.send(body);
}

const drop = $('.drop');
drop.ondragover = event => { event.preventDefault(); drop.classList.add('drag'); };
drop.ondragleave = () => drop.classList.remove('drag');
drop.ondrop = event => {
  event.preventDefault();
  drop.classList.remove('drag');
  [...event.dataTransfer.files].forEach(upload);
};
$('#picker').onchange = event => [...event.target.files].forEach(upload);
$('#up').onclick = () => {
  const crumbs = [...$('#path').querySelectorAll('button')];
  if (crumbs.length > 1) crumbs[crumbs.length - 2].click();
};
$('#download-tab').onclick = () => selectTab('download');
$('#upload-tab').onclick = () => selectTab('upload');
$('#close-preview').onclick = () => $('#preview-dialog').close();
$('#preview-dialog').onclick = event => { if (event.target === $('#preview-dialog')) $('#preview-dialog').close(); };
$('#save-text').onclick = async () => {
  const result = $('#text-result');
  result.textContent = '正在上传…';
  try {
    const payload = await api('upload-text', { method: 'PUT', body: $('#text').value });
    result.textContent = `已上传 ${payload.characters} 字`;
  } catch (error) {
    result.textContent = `上传失败：${error.message}`;
  }
};

async function init() {
  try {
    const status = await api('status');
    $('#download-tab').hidden = !status.downloadEnabled;
    $('#upload-tab').hidden = !status.uploadEnabled;
    if (status.downloadEnabled) {
      $('#path').title = `下载目录：${status.downloadDir}`;
      selectTab('download');
      files();
    } else if (status.uploadEnabled) selectTab('upload');
    else {
      $('.tabs').hidden = true;
      $('#download-panel').hidden = true;
      $('#upload-panel').hidden = true;
    }
    $('#status').textContent = '已连接';
  } catch (error) {
    $('#status').textContent = error.message;
  }
}

init();
