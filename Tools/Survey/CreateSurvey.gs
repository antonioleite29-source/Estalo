/**
 * Builds the Estalo user-testing survey as a Google Form.
 *
 * 1. Go to https://script.google.com and create a new project.
 * 2. Paste this whole file over the empty Code.gs.
 * 3. Pick createSurvey in the function menu and press Run. Allow the permission prompt.
 * 4. The links to fill in and to edit the form are printed in the Execution log.
 *
 * Set LANG to 'en' to build the English copy instead (for reviewers who don't read
 * Portuguese). Every run makes a new form; delete old drafts from Google Drive.
 */
const LANG = 'pt';

const SURVEY = {
  title: {
    pt: 'Estalo — Teste com jogadores',
    en: 'Estalo — Player testing',
  },
  description: {
    pt: 'Obrigado por testar o Estalo! Responda logo depois de jogar, enquanto ainda lembra. ' +
        'Leva uns 5 minutos, é anônimo e não existe resposta errada — se algo foi ruim, queremos saber.',
    en: 'Thanks for testing Estalo! Please answer right after playing, while it is fresh. ' +
        'It takes about 5 minutes, it is anonymous, and there are no wrong answers — if something was bad, we want to know.',
  },
  sections: [
    {
      title: { pt: 'Sobre você', en: 'About you' },
      items: [
        {
          type: 'text', required: false,
          title: { pt: 'Número do teste (quem organizou o teste te passa)', en: 'Test number (the organiser gives you this)' },
        },
        {
          type: 'choice', required: true,
          title: { pt: 'Qual a sua idade?', en: 'How old are you?' },
          choices: {
            pt: ['Menos de 10', '10 a 13', '14 a 17', '18 a 25', '26 a 40', 'Mais de 40'],
            en: ['Under 10', '10 to 13', '14 to 17', '18 to 25', '26 to 40', 'Over 40'],
          },
        },
        {
          type: 'choice', required: true,
          title: { pt: 'Você conhece quem fez o jogo?', en: 'Do you know the person who made the game?' },
          choices: {
            pt: ['Sim, sou da família ou amigo', 'Não, não conheço'],
            en: ['Yes, family or friend', 'No, I don\'t know them'],
          },
        },
        {
          type: 'choice', required: true,
          title: { pt: 'Com que frequência você joga no celular?', en: 'How often do you play games on your phone?' },
          choices: {
            pt: ['Todo dia', 'Algumas vezes por semana', 'Raramente', 'Nunca'],
            en: ['Every day', 'A few times a week', 'Rarely', 'Never'],
          },
        },
        {
          type: 'scale', required: true, low: 1, high: 5,
          title: { pt: 'O que você acha de matemática?', en: 'How do you feel about maths?' },
          labels: { pt: ['Não gosto', 'Adoro'], en: ['I dislike it', 'I love it'] },
        },
        {
          type: 'text', required: false,
          title: { pt: 'Qual celular você usou? (ex.: Samsung A15, iPhone 13)', en: 'Which phone did you use? (e.g. Samsung A15, iPhone 13)' },
        },
      ],
    },
    {
      title: { pt: 'Primeiros minutos', en: 'First minutes' },
      items: [
        {
          type: 'scale', required: true, low: 1, high: 5,
          title: { pt: 'O tutorial deixou claro como jogar?', en: 'Did the tutorial make it clear how to play?' },
          labels: { pt: ['Nada claro', 'Muito claro'], en: ['Not clear at all', 'Very clear'] },
        },
        {
          type: 'choice', required: true,
          title: {
            pt: 'Quando você entendeu a regra do erro (errou → o outro lado responde sozinho no tempo)?',
            en: 'When did you understand the wrong-answer rule (wrong → the other side answers alone, on a timer)?',
          },
          choices: {
            pt: ['Logo no tutorial', 'Depois da primeira rodada', 'Só no fim da partida', 'Não entendi'],
            en: ['Straight away in the tutorial', 'After the first round', 'Only by the end of the match', 'I didn\'t understand it'],
          },
        },
        {
          type: 'paragraph', required: false,
          title: { pt: 'Teve alguma parte confusa no começo? Qual?', en: 'Was anything confusing at the start? What?' },
        },
      ],
    },
    {
      title: { pt: 'A partida', en: 'The match' },
      items: [
        {
          type: 'checkbox', required: true,
          title: { pt: 'O que você jogou?', en: 'What did you play?' },
          choices: {
            pt: ['Partida 1 contra 1', 'Partida 2 contra 2', 'Praticar meus erros', 'Trilha de lições'],
            en: ['1v1 match', '2v2 match', 'Practise my mistakes', 'Lesson path'],
          },
        },
        {
          type: 'scale', required: true, low: 1, high: 5,
          title: { pt: 'Quão divertida foi a partida?', en: 'How fun was the match?' },
          labels: { pt: ['Chata', 'Muito divertida'], en: ['Boring', 'Really fun'] },
        },
        {
          type: 'choice', required: true,
          title: { pt: 'As perguntas estavam…', en: 'The questions were…' },
          choices: {
            pt: ['Fáceis demais', 'Um pouco fáceis', 'Na medida', 'Um pouco difíceis', 'Difíceis demais'],
            en: ['Far too easy', 'A bit easy', 'About right', 'A bit hard', 'Far too hard'],
          },
        },
        {
          type: 'choice', required: true,
          title: { pt: 'O tempo para responder sozinho (depois que o outro errou) foi…', en: 'The time to answer alone (after the other side got it wrong) was…' },
          choices: {
            pt: ['Curto demais', 'Na medida', 'Longo demais', 'Não aconteceu comigo'],
            en: ['Too short', 'About right', 'Too long', 'It never happened to me'],
          },
        },
        {
          type: 'checkbox', required: true, other: true,
          title: { pt: 'Aconteceu algum problema?', en: 'Did anything go wrong?' },
          choices: {
            pt: ['Nenhum problema', 'Caiu a conexão', 'Ficou lento ou travou', 'Toquei e o botão não respondeu',
                 'Texto cortado ou pequeno demais', 'Demorou para achar adversário'],
            en: ['Nothing went wrong', 'Lost connection', 'Slow or froze', 'Tapped and the button didn\'t respond',
                 'Text cut off or too small', 'Took too long to find an opponent'],
          },
        },
        {
          type: 'paragraph', required: false,
          title: { pt: 'Se deu problema, conta o que você estava fazendo na hora.', en: 'If something went wrong, what were you doing at the time?' },
        },
      ],
    },
    {
      title: { pt: 'Visual', en: 'Look and feel' },
      items: [
        {
          type: 'scale', required: true, low: 1, high: 5,
          title: { pt: 'O visual do jogo (cores, botões, telas) parece…', en: 'The game\'s look (colours, buttons, screens) feels…' },
          labels: { pt: ['Amador', 'Profissional'], en: ['Amateur', 'Professional'] },
        },
        {
          type: 'scale', required: true, low: 1, high: 5,
          title: { pt: 'Foi fácil ler as perguntas e respostas?', en: 'Was it easy to read the questions and answers?' },
          labels: { pt: ['Muito difícil', 'Muito fácil'], en: ['Very hard', 'Very easy'] },
        },
        {
          type: 'paragraph', required: false,
          title: { pt: 'O que você mudaria no visual?', en: 'What would you change about how it looks?' },
        },
      ],
    },
    {
      title: { pt: 'Para terminar', en: 'To finish' },
      items: [
        {
          type: 'scale', required: true, low: 1, high: 5,
          title: { pt: 'O jogo ajuda a praticar matemática?', en: 'Does the game help you practise maths?' },
          labels: { pt: ['Nada', 'Muito'], en: ['Not at all', 'A lot'] },
        },
        {
          type: 'choice', required: true,
          title: { pt: 'Você jogaria de novo por conta própria?', en: 'Would you play again on your own?' },
          choices: {
            pt: ['Sim, com certeza', 'Talvez', 'Não'],
            en: ['Yes, definitely', 'Maybe', 'No'],
          },
        },
        {
          type: 'scale', required: true, low: 0, high: 10,
          title: { pt: 'De 0 a 10, quanto você indicaria o Estalo para um amigo?', en: 'From 0 to 10, how likely are you to recommend Estalo to a friend?' },
          labels: { pt: ['Nunca', 'Com certeza'], en: ['Never', 'Definitely'] },
        },
        {
          type: 'choice', required: true,
          title: { pt: 'Para salvar seu progresso, você criaria uma conta com…', en: 'To save your progress, you would make an account with…' },
          choices: {
            pt: ['Google', 'Apple', 'E-mail e senha', 'Não criaria conta'],
            en: ['Google', 'Apple', 'Email and password', 'I wouldn\'t make an account'],
          },
        },
        {
          type: 'checkbox', required: true, other: true,
          title: { pt: 'Onde você costuma descobrir jogos novos?', en: 'Where do you usually find out about new games?' },
          choices: {
            pt: ['TikTok', 'Instagram', 'YouTube', 'WhatsApp', 'Amigos ou escola', 'Loja de apps'],
            en: ['TikTok', 'Instagram', 'YouTube', 'WhatsApp', 'Friends or school', 'App store'],
          },
        },
        {
          type: 'paragraph', required: false,
          title: { pt: 'Do que você mais gostou?', en: 'What did you like most?' },
        },
        {
          type: 'paragraph', required: true,
          title: { pt: 'Se pudesse mudar uma coisa, o que seria?', en: 'If you could change one thing, what would it be?' },
        },
      ],
    },
  ],
};

function createSurvey() {
  const form = FormApp.create(SURVEY.title[LANG]);
  form.setDescription(SURVEY.description[LANG])
      .setProgressBar(true)
      .setCollectEmail(false)
      .setAllowResponseEdits(false)
      .setConfirmationMessage(LANG === 'pt' ? 'Valeu! Sua resposta ajuda muito.' : 'Thanks! Your answer really helps.');

  SURVEY.sections.forEach((section, i) => {
    // The first section sits on the form's opening page; every later one gets its own page.
    if (i === 0) form.addSectionHeaderItem().setTitle(section.title[LANG]);
    else form.addPageBreakItem().setTitle(section.title[LANG]);

    section.items.forEach(q => addItem(form, q));
  });

  Logger.log('Link to share:  ' + form.getPublishedUrl());
  Logger.log('Link to edit:   ' + form.getEditUrl());
}

function addItem(form, q) {
  const title = q.title[LANG];
  let item;
  switch (q.type) {
    case 'text':
      item = form.addTextItem().setTitle(title);
      break;
    case 'paragraph':
      item = form.addParagraphTextItem().setTitle(title);
      break;
    case 'choice':
      item = form.addMultipleChoiceItem().setTitle(title).setChoiceValues(q.choices[LANG]);
      if (q.other) item.showOtherOption(true);
      break;
    case 'checkbox':
      item = form.addCheckboxItem().setTitle(title).setChoiceValues(q.choices[LANG]);
      if (q.other) item.showOtherOption(true);
      break;
    case 'scale':
      item = form.addScaleItem().setTitle(title).setBounds(q.low, q.high)
                 .setLabels(q.labels[LANG][0], q.labels[LANG][1]);
      break;
    default:
      throw new Error('Unknown question type: ' + q.type);
  }
  item.setRequired(q.required);
}
