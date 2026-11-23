"""Verify email codes through local HTTPS and an isolated SMTP STARTTLS fixture."""
from email import policy
from email.parser import Parser
import os
import queue
import re
import secrets
import socketserver
import sqlite3
import ssl
import subprocess
import tempfile
import threading
import time
from pathlib import Path

from customer_admin_http import Client, token

base = 'https://localhost:5454'
password = secrets.token_urlsafe(24) + 'Aa1!'
admin_email = 'otp-admin@example.test'
customer_email = 'otp-customer@example.test'
messages = queue.Queue()


class MailFixture(socketserver.StreamRequestHandler):
    def reply(self, response):
        self.wfile.write(response)

    def handle(self):
        self.reply(b'220 localhost test SMTP\r\n')
        encrypted = False
        while line := self.rfile.readline():
            command = line.split(maxsplit=1)[0].upper()
            if command == b'EHLO':
                self.reply(b'250 localhost\r\n' if encrypted else b'250-localhost\r\n250 STARTTLS\r\n')
            elif command == b'STARTTLS':
                self.reply(b'220 Ready\r\n')
                self.connection = self.server.tls.wrap_socket(self.connection, server_side=True)
                self.rfile = self.connection.makefile('rb')
                self.wfile = self.connection.makefile('wb', buffering=0)
                encrypted = True
            elif command == b'DATA' and encrypted:
                self.reply(b'354 End with dot\r\n')
                lines = []
                while (part := self.rfile.readline()) not in (b'.\r\n', b''):
                    lines.append(part)
                messages.put(b''.join(lines).decode())
                self.reply(b'250 Queued\r\n')
            elif command in (b'MAIL', b'RCPT') and encrypted:
                self.reply(b'250 OK\r\n')
            elif command == b'QUIT':
                self.reply(b'221 Bye\r\n')
                return
            else:
                self.reply(b'503 TLS required\r\n')


def otp_for(email):
    message = Parser(policy=policy.default).parsestr(messages.get(timeout=10))
    assert message['To'] == email
    return re.search(r'رمز التحقق: (\d{6})', message.get_content())[1]


with tempfile.TemporaryDirectory() as directory:
    folder = Path(directory)
    cert, key = folder / 'cert.pem', folder / 'key.pem'
    subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                    '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost',
                    '-keyout', str(key), '-out', str(cert)], stderr=subprocess.DEVNULL, check=True)
    with socketserver.ThreadingTCPServer(('127.0.0.1', 0), MailFixture) as smtp:
        smtp.tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        smtp.tls.load_cert_chain(cert, key)
        threading.Thread(target=smtp.serve_forever, daemon=True).start()
        env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=base,
                   EmailOtp__Enabled='true', Database__UseDevelopmentSqlite='true',
                   Database__DevelopmentSqlitePath=str(folder / 'otp.db'),
                   AdminSettings__Email=admin_email, AdminSettings__Password=password,
                   StoreWorker__ReconcilePayments='false', NilePay__Enabled='false',
                   NilePay__PublicUrl=base, Smtp__Enabled='true', Smtp__Host='  localhost  ', Smtp__Port=str(smtp.server_address[1]),
                   Smtp__EnableSsl='true', Smtp__From='  sender@example.test  ',
                   Smtp__Username='', Smtp__Password='', SSL_CERT_FILE=str(cert))
        with (folder / 'server.log').open('w') as log:
            proc = subprocess.Popen(['.dotnet/dotnet', 'bin/Debug/net10.0/NileShop.dll'],
                                    env=env, stdout=log, stderr=log)
            try:
                customer, admin, other = Client(base), Client(base), Client(base)
                route = '/Account/VerifyEmailCode'
                for _ in range(120):
                    try:
                        if customer.req('/')[0] == 200:
                            break
                    except OSError:
                        pass
                    if proc.poll() is not None:
                        raise AssertionError('OTP test site failed to start')
                    time.sleep(.5)
                else:
                    raise AssertionError('OTP test site did not become ready')

                # Hosting SMTP must recover account email even with stale encrypted database settings.
                with sqlite3.connect(folder / 'otp.db') as db:
                    db.execute("UPDATE StoreMailSettings SET Enabled=1, EncryptedPassword='unreadable'")
                _, page, _ = customer.req('/Account/Register')
                _, verify, url = customer.req('/Account/Register', {
                    'Email': customer_email, 'FullName': 'OTP Customer', 'Phone': '01012345678',
                    'Password': password, 'ConfirmPassword': password, 'PreferredLanguage': 'en',
                    '__RequestVerificationToken': token(page)})
                assert url.endswith(route) and 'autocomplete="one-time-code"' in verify
                assert '/Account/Login' in customer.req('/Account/Orders')[2]
                code = otp_for(customer_email)
                _, home, _ = other.req('/')
                assert 'no longer valid' in other.req(route, {'code': code, '__RequestVerificationToken': token(home)})[1]
                wrong = '000000' if code != '000000' else '000001'
                _, invalid, _ = customer.req(route, {'code': wrong, '__RequestVerificationToken': token(verify)})
                assert 'no longer valid' in invalid
                assert customer.req(route, {'code': code, '__RequestVerificationToken': token(invalid)})[2] == base + '/'
                _, orders, url = customer.req('/Account/Orders')
                assert url.endswith('/Account/Orders')
                assert 'no longer valid' in customer.req(route, {'code': code, '__RequestVerificationToken': token(orders)})[1]
                print('PASS signup requires a session-bound valid code and rejects replay', flush=True)

                _, login, _ = customer.req('/Account')
                customer.req('/Account/Logout', {'__RequestVerificationToken': token(login)})
                _, login, _ = customer.req('/Account/Login')
                _, verify, url = customer.req('/Account/Login', {
                    'Email': customer_email, 'Password': password, 'ReturnUrl': '/Account/Orders',
                    '__RequestVerificationToken': token(login)})
                assert url.endswith(route)
                assert customer.req(route, {'code': otp_for(customer_email),
                                            '__RequestVerificationToken': token(verify)})[2].endswith('/Account/Orders')
                print('PASS customer login requires a fresh email code and restores the destination', flush=True)

                _, login, _ = admin.req('/Identity/Account/Login')
                _, verify, url = admin.req('/Identity/Account/Login', {
                    'Email': admin_email, 'Password': password, '__RequestVerificationToken': token(login)})
                assert url.endswith(route)
                assert '/Identity/Account/Login' in admin.req('/Admin/Dashboard')[2]
                assert admin.req(route, {'code': otp_for(admin_email),
                                         '__RequestVerificationToken': token(verify)})[2].endswith('/Admin/Dashboard')
                print('PASS admin dashboard stays restricted until verification', flush=True)

                # Admin-only publishing, durable inboxes, ownership and anti-forgery checks.
                import uuid
                assert '/Account/Login' in other.req('/Account/Notifications')[2]
                assert 'notification-compose' not in customer.req('/Admin/Notifications')[1]
                _, compose, _ = admin.req('/Admin/Notifications')
                key = str(uuid.uuid4())
                payload = {'SendKey': key, 'Title': 'Private <script>alert(1)</script>',
                           'Body': 'Customer-only message', 'RecipientEmail': customer_email,
                           '__RequestVerificationToken': token(compose)}
                assert admin.req('/Admin/Notifications', payload)[0] == 200
                admin.req('/Admin/Notifications', payload)
                with sqlite3.connect(folder / 'otp.db') as db:
                    assert db.execute('SELECT COUNT(*) FROM ClientNotifications').fetchone()[0] == 1
                    notification_id = db.execute('SELECT Id FROM ClientNotifications').fetchone()[0]
                _, inbox, _ = customer.req('/Account/Notifications')
                assert 'Customer-only message' in inbox and '<script>alert(1)</script>' not in inbox
                assert customer.req('/Account/Notifications/Read', {'id': notification_id})[0] == 400
                assert admin.req('/Account/Notifications/Read', {'id': notification_id,
                    '__RequestVerificationToken': token(compose)})[0] == 404
                assert customer.req('/Account/Notifications/Read', {'id': notification_id,
                    '__RequestVerificationToken': token(inbox)})[0] == 200
                import json
                assert json.loads(customer.req('/Account/Notifications/Unread')[1])['count'] == 0
                payload.update(SendKey=str(uuid.uuid4()), RecipientEmail='', Title='Store announcement', Body='For everyone')
                admin.req('/Admin/Notifications', payload)
                assert json.loads(customer.req('/Account/Notifications/Unread')[1])['count'] == 1
                assert 'For everyone' in customer.req('/Account/Notifications')[1]
                print('PASS notification broadcast, targeting, read state, replay protection, escaping and CSRF', flush=True)

                from urllib.parse import parse_qs, urlsplit

                recovery = Client(base)
                forgot = '/Account/Login/ForgotPassword'
                _, page, _ = recovery.req(forgot)
                _, known, known_url = recovery.req(forgot, {
                    'Email': customer_email, '__RequestVerificationToken': token(page)})
                message = Parser(policy=policy.default).parsestr(messages.get(timeout=10))
                assert message['To'] == customer_email
                link = urlsplit(next(line for line in message.get_content().splitlines() if line.startswith('https://')))
                assert link.netloc == urlsplit(base).netloc
                fields = parse_qs(link.query)
                assert '/Account/Login/ResetPassword' == link.path
                _, page, _ = recovery.req(forgot)
                _, unknown, unknown_url = recovery.req(forgot, {
                    'Email': 'unknown@example.test', '__RequestVerificationToken': token(page)})
                assert known_url == unknown_url and 'If an account exists' in known and 'If an account exists' in unknown
                assert messages.empty()

                _, reset, _ = recovery.req(link.path + '?' + link.query)
                new_password = secrets.token_urlsafe(24) + 'Bb2!'
                payload = {'UserId': fields['userId'][0], 'Token': fields['token'][0],
                           'Password': new_password, 'ConfirmPassword': new_password,
                           '__RequestVerificationToken': token(reset)}
                _, login, url = recovery.req(link.path, payload)
                assert url.endswith('/Account/Login') and 'Password updated' in login
                assert 'invalid or has expired' in recovery.req(link.path, payload)[1]
                _, rejected, _ = recovery.req('/Account/Login', {
                    'Email': customer_email, 'Password': password, '__RequestVerificationToken': token(login)})
                assert 'id="otp-code"' not in rejected and messages.empty()
                _, verify, url = recovery.req('/Account/Login', {
                    'Email': customer_email, 'Password': new_password, '__RequestVerificationToken': token(rejected)})
                assert url.endswith(route) and 'id="otp-code"' in verify
                otp_for(customer_email)
                print('PASS reset links are one-time, responses are generic, and the new password still requires OTP', flush=True)
            finally:
                proc.terminate()
                proc.wait(timeout=15)
                smtp.shutdown()
