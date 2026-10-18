import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 50,
  duration: '60s',
  thresholds: {
    http_req_duration: ['p(95)<500'], 
    http_req_failed: ['rate<0.01'],   
  },
};

export default function () {
  // Target the staging backend port you configured in .env.staging
  const res = http.get('http://localhost:5062/health');
  
  check(res, {
    'status is 200': (r) => r.status === 200,
  });
  
  sleep(1); 
}