const json = JSON.stringify({
  data: [
    {id: '15493439', type: 'campaign', attributes: {name: 'Victor7996', url: 'https://www.patreon.com/Victor7996'}},
    {id: '9876543', type: 'campaign', attributes: {name: 'Beaver Gaming', url: 'https://www.patreon.com/BeaverGaming'}}
  ]
});

const obj = JSON.parse(json);
const campaigns = obj.data.map(item => ({
  id: item.id,
  name: item.attributes ? item.attributes.name : '',
  url: item.attributes ? item.attributes.url : ''
}));

console.log('Parsed campaigns:', campaigns);
