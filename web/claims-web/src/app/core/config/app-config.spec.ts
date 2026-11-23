import { parseAppConfig } from './app-config';

describe('parseAppConfig', () => {
  it('accepts an absolute URL and strips trailing slashes', () => {
    expect(parseAppConfig({ apiBaseUrl: 'https://claims-api.azurewebsites.net//' })).toEqual({
      apiBaseUrl: 'https://claims-api.azurewebsites.net',
    });
  });

  it.each([null, {}, { apiBaseUrl: '' }, { apiBaseUrl: '/api' }, { apiBaseUrl: 42 }, { apiBaseUrl: 'ftp://x' }])(
    'rejects %j',
    (raw) => {
      expect(() => parseAppConfig(raw)).toThrow(/apiBaseUrl/);
    },
  );
});
